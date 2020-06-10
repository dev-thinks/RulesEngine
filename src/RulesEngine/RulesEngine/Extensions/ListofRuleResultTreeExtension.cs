// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using RulesEngine.Models;
using System.Collections.Generic;
using System.Linq;

namespace RulesEngine.Extensions
{
    public static class ListofRuleResultTreeExtension
    {
        public delegate void OnSuccessFunc(string eventName);
        public delegate void OnFailureFunc(List<RuleResultTree> failedRulesResult);

        public static List<RuleResultTree> OnSuccess(this List<RuleResultTree> ruleResultTrees, OnSuccessFunc onSuccessFunc)
        {
            var successfulRuleResult = ruleResultTrees.FirstOrDefault(ruleResult => ruleResult.IsSuccess);
            if (successfulRuleResult != null)
            {
                var eventName = successfulRuleResult.Rule.SuccessEvent ?? successfulRuleResult.Rule.RuleName;
                onSuccessFunc(eventName);
            }

            return ruleResultTrees;
        }

        public static List<RuleResultTree> OnFail(this List<RuleResultTree> ruleResultTrees, OnFailureFunc onFailureFunc)
        {
            var failedRulesResult = ruleResultTrees.Where(ruleResult => ruleResult.IsSuccess == false).ToList();
            if (failedRulesResult.Count > 0)
            {
                onFailureFunc(failedRulesResult);
            }

            return ruleResultTrees;
        }
    }
}
