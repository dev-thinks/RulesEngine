// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RulesEngine.HelperFunctions;
using RulesEngine.Interfaces;
using RulesEngine.Models;
using RulesEngine.Validators;
using RulesEngine.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using FluentValidation;

namespace RulesEngine
{
    public class RulesEngine : IRulesEngine
    {
        #region Variables
        private readonly WorkflowRules[] _workflowRules;
        private readonly ILogger _logger;
        private readonly ReSettings _reSettings;
        private readonly RulesCache _rulesCache = new RulesCache();
        #endregion

        #region Constructor
        public RulesEngine(string[] jsonConfig, ILogger logger, ReSettings reSettings = null) : this(logger, reSettings: reSettings)
        {
            var workflowRules = jsonConfig.Select(JsonConvert.DeserializeObject<WorkflowRules>).ToArray();

            _workflowRules = workflowRules;

            AddWorkflow(workflowRules);
        }

        public RulesEngine(WorkflowRules[] workflowRules, ILogger logger, ReSettings reSettings = null) : this(logger, workflowRules, reSettings)
        {
            AddWorkflow(workflowRules);
        }

        public RulesEngine(ILogger logger, WorkflowRules[] workflowRules = null, ReSettings reSettings = null)
        {
            _workflowRules = workflowRules;
            _logger = logger ?? new NullLogger<RulesEngine>();
            _reSettings = reSettings ?? new ReSettings();
        }
        #endregion

        #region Public Methods

        /// <summary>
        /// This will execute all the rules of the specified workflow
        /// </summary>
        /// <param name="workflowName">The name of the workflow with rules to execute against the inputs</param>
        /// <param name="inputs">A variable number of inputs</param>
        /// <returns>List of rule results</returns>
        public List<RuleResultTree> ExecuteRule(string workflowName, params object[] inputs)
        {
            _logger.LogTrace("Called ExecuteRule for workflow {WorkflowName} and count of input {Inputs}", workflowName, inputs.Length);

            var ruleParams = new List<RuleParameter>();

            for (int i = 0; i < inputs.Length; i++)
            {
                var input = inputs[i];
                var obj = Utils.GetTypedObject(input);
                ruleParams.Add(new RuleParameter($"input{i + 1}", obj));
            }

            return ExecuteRule(workflowName, ruleParams.ToArray());
        }

        /// <summary>
        /// This will execute all the rules of the specified workflow
        /// </summary>
        /// <param name="workflowName">The name of the workflow with rules to execute against the inputs</param>
        /// <param name="ruleParams">A variable number of rule parameters</param>
        /// <returns>List of rule results</returns>
        public List<RuleResultTree> ExecuteRule(string workflowName, params RuleParameter[] ruleParams)
        {
            return ValidateWorkflowAndExecuteRule(workflowName, ruleParams);
        }

        /// <inheritdoc />
        public async Task<List<RuleResultTree>> ResolveAndExecuteRule(string workflowName, params object[] inputs)
        {
            foreach (var wk in _workflowRules)
            {
                if (wk.Rules == null) continue;

                foreach (var wkRule in wk.Rules)
                {
                    if (wkRule.Endpoints.Count > 0)
                    {
                        foreach (var apiInputConfig in wkRule.Endpoints)
                        {
                            string dynamicValue;
                            var expressionName = apiInputConfig.ExpressionName;

                            try
                            {
                                var response = await ApiHelper.Get<dynamic>(apiInputConfig.Uri);

                                dynamicValue = ((IDictionary<string, object>)response)
                                    [expressionName.Replace("$", "")].ToString();
                            }
                            catch (Exception e)
                            {
                                _logger.LogError(e,
                                    "Ensure Api response has a field matches with ExpressionName: {RuleName}",
                                    wkRule.RuleName);

                                dynamicValue = "API_ERROR";
                            }

                            wkRule.Expression = wkRule.Expression.Replace(expressionName, dynamicValue);
                        }
                    }
                }
            }

            AddWorkflow(_workflowRules);

            return ExecuteRule(workflowName, inputs);
        }

        #endregion

        #region Private Methods

        public void AddWorkflow(params WorkflowRules[] workflowRules)
        {
            try
            {
                foreach (var workflowRule in workflowRules)
                {
                    var validator = new WorkflowRulesValidator();
                    validator.ValidateAndThrow(workflowRule);
                    _rulesCache.AddOrUpdateWorkflowRules(workflowRule.WorkflowName, workflowRule);
                }
            }
            catch (ValidationException ex)
            {
                throw new RuleValidationException(ex.Message, ex.Errors);
            }
        }

        public void ClearWorkflows()
        {
            _rulesCache.Clear();
        }

        public void RemoveWorkflow(params string[] workflowNames)
        {
            foreach (var workflowName in workflowNames)
            {
                _rulesCache.Remove(workflowName);
            }
        }

        /// <summary>
        /// This will validate workflow rules then call execute method
        /// </summary>
        /// <typeparam name="T">type of entity</typeparam>
        /// <param name="input">input</param>
        /// <param name="workflowName">workflow name</param>
        /// <returns>list of rule result set</returns>
        private List<RuleResultTree> ValidateWorkflowAndExecuteRule(string workflowName, RuleParameter[] ruleParams)
        {
            List<RuleResultTree> result;

            if (RegisterRule(workflowName, ruleParams))
            {
                result = ExecuteRuleByWorkflow(workflowName, ruleParams);
            }
            else
            {
                _logger.LogTrace($"Rule config file is not present for the {workflowName} workflow");
                // if rules are not registered with Rules Engine
                throw new ArgumentException($"Rule config file is not present for the {workflowName} workflow");
            }
            return result;
        }


        /// <summary>
        /// This will compile the rules and store them to dictionary
        /// </summary>
        /// <typeparam name="T">type of entity</typeparam>
        /// <param name="workflowName">workflow name</param>
        /// <returns>bool result</returns>
        private bool RegisterRule(string workflowName, params RuleParameter[] ruleParams)
        {
            string compileRulesKey = GetCompileRulesKey(workflowName, ruleParams);
            if (_rulesCache.ContainsCompiledRules(compileRulesKey))
                return true;

            var workflowRules = _rulesCache.GetWorkFlowRules(workflowName);

            if (workflowRules != null)
            {
                var lstFunc = new List<Delegate>();
                foreach (var rule in _rulesCache.GetRules(workflowName))
                {
                    RuleCompiler ruleCompiler = new RuleCompiler(new RuleExpressionBuilderFactory(_reSettings), _logger);
                    lstFunc.Add(ruleCompiler.CompileRule(rule, ruleParams));
                }

                _rulesCache.AddOrUpdateCompiledRule(compileRulesKey, new CompiledRule() { CompiledRules = lstFunc });
                _logger.LogTrace($"Rules has been compiled for the {workflowName} workflow and added to dictionary");
                return true;
            }
            else
            {
                return false;
            }
        }

        private static string GetCompileRulesKey(string workflowName, RuleParameter[] ruleParams)
        {
            return $"{workflowName}-" + String.Join("-", ruleParams.Select(c => c.Type.Name));
        }

        /// <summary>
        /// This will execute the compiled rules 
        /// </summary>
        /// <param name="workflowName"></param>
        /// <param name="ruleParams"></param>
        /// <returns>list of rule result set</returns>
        private List<RuleResultTree> ExecuteRuleByWorkflow(string workflowName, RuleParameter[] ruleParams)
        {
            _logger.LogTrace($"Compiled rules found for {workflowName} workflow and executed");

            List<RuleResultTree> result = new List<RuleResultTree>();
            var compileRulesKey = GetCompileRulesKey(workflowName, ruleParams);
            var inputs = ruleParams.Select(c => c.Value);
            foreach (var compiledRule in _rulesCache.GetCompiledRules(compileRulesKey).CompiledRules)
            {
                result.Add(compiledRule.DynamicInvoke(new List<object>(inputs) { new RuleInput() }.ToArray()) as RuleResultTree);
            }

            return result;
        }
        #endregion
    }
}
