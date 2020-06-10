// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using RulesEngine.Exceptions;
using RulesEngine.HelperFunctions;
using RulesEngine.Interfaces;
using RulesEngine.Models;
using RulesEngine.Validators;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RulesEngine
{
    /// <inheritdoc />
    public class RulesEngine : IRulesEngine
    {
        private readonly WorkflowRules[] _workflowRules;
        private readonly ILogger _logger;
        private readonly ReSettings _reSettings;
        private readonly RulesCache _rulesCache = new RulesCache();

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="jsonConfig"></param>
        /// <param name="logger"></param>
        /// <param name="reSettings"></param>
        public RulesEngine(string[] jsonConfig, ILogger logger, ReSettings reSettings = null) : this(logger, reSettings: reSettings)
        {
            var workflowRules = jsonConfig.Select(JsonConvert.DeserializeObject<WorkflowRules>).ToArray();

            _workflowRules = workflowRules;

            AddWorkflow(workflowRules);
        }

        /// <summary>
        /// .ctor
        /// </summary>
        /// <param name="workflowRules"></param>
        /// <param name="logger"></param>
        /// <param name="reSettings"></param>
        public RulesEngine(WorkflowRules[] workflowRules, ILogger logger, ReSettings reSettings = null) : this(logger, workflowRules, reSettings)
        {
            AddWorkflow(workflowRules);
        }

        /// <summary>
        /// base .ctor
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="workflowRules"></param>
        /// <param name="reSettings"></param>
        public RulesEngine(ILogger logger, WorkflowRules[] workflowRules = null, ReSettings reSettings = null)
        {
            _workflowRules = workflowRules;
            _logger = logger ?? new NullLogger<RulesEngine>();
            _reSettings = reSettings ?? new ReSettings();
        }

        /// <inheritdoc />
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

        /// <inheritdoc />
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
                            var response = await ApiHelper.Get<dynamic>(apiInputConfig.Uri);

                            var expressionName = apiInputConfig.ExpressionName;

                            var dynamicValue = ((IDictionary<string, object>) response)
                                [expressionName.Replace("$", "")].ToString();

                            wkRule.Expression = wkRule.Expression.Replace(expressionName, dynamicValue);
                        }
                    }
                }
            }

            AddWorkflow(_workflowRules);

            return ExecuteRule(workflowName, inputs);
        }

        /// <summary>
        /// Clears the workflow
        /// </summary>
        public void ClearWorkflows()
        {
            _rulesCache.Clear();
        }

        /// <summary>
        /// Remove workflow from cache
        /// </summary>
        /// <param name="workflowNames"></param>
        public void RemoveWorkflow(params string[] workflowNames)
        {
            foreach (var workflowName in workflowNames)
            {
                _rulesCache.Remove(workflowName);
            }
        }

        /// <summary>
        /// Adds or updates the workflow
        /// </summary>
        /// <param name="workflowRules"></param>
        private void AddWorkflow(params WorkflowRules[] workflowRules)
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

        /// <summary>
        /// This will validate workflow rules then call execute method
        /// </summary>
        /// <param name="workflowName">workflow name</param>
        /// <param name="ruleParams"></param>
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
                _logger.LogTrace("Rule config file is not present for the {WorkflowName} workflow", workflowName);

                // if rules are not registered with Rules Engine
                throw new ArgumentException($"Rule config file is not present for the {workflowName} workflow");
            }

            return result;
        }

        /// <summary>
        /// This will compile the rules and store them to dictionary
        /// </summary>
        /// <param name="workflowName">workflow name</param>
        /// <param name="ruleParams"></param>
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
                _logger.LogTrace("Rules has been compiled for the {WorkflowName} workflow and added to dictionary", workflowName);

                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Gets compile rule key
        /// </summary>
        /// <param name="workflowName"></param>
        /// <param name="ruleParams"></param>
        /// <returns></returns>
        private static string GetCompileRulesKey(string workflowName, RuleParameter[] ruleParams)
        {
            return $"{workflowName}-" + string.Join("-", ruleParams.Select(c => c.Type.Name));
        }

        /// <summary>
        /// This will execute the compiled rules 
        /// </summary>
        /// <param name="workflowName"></param>
        /// <param name="ruleParams"></param>
        /// <returns>list of rule result set</returns>
        private List<RuleResultTree> ExecuteRuleByWorkflow(string workflowName, RuleParameter[] ruleParams)
        {
            _logger.LogTrace("Compiled rules found for {WorkflowName} workflow and executed", workflowName);

            List<RuleResultTree> result = new List<RuleResultTree>();
            var compileRulesKey = GetCompileRulesKey(workflowName, ruleParams);
            var inputs = ruleParams.Select(c => c.Value);

            foreach (var compiledRule in _rulesCache.GetCompiledRules(compileRulesKey).CompiledRules)
            {
                result.Add(compiledRule.DynamicInvoke(new List<object>(inputs) { new RuleInput() }.ToArray()) as RuleResultTree);
            }

            return result;
        }
    }
}
