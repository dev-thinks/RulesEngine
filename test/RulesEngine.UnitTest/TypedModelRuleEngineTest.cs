// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using Moq;
using Newtonsoft.Json;
using RulesEngine.Models;
using RulesEngine.UnitTest.Models;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RulesEngine.UnitTest
{
    [Trait("Category", "Unit")]
    public class TypedModelRuleEngineTest
    {
        [Theory]
        [InlineData("rules4.json")]
        public void ExecuteRule_ReturnsListOfRuleResultTree_ResultMessage(string ruleFileName)
        {
            var re = GetRulesEngine(ruleFileName);

            Person input1 = GetInput1();

            var result = re.ExecuteRule("inputWorkflow", input1);

            var allMessages = result.First().GetMessages();
            var warningMessages = result.First().GetMessages().WarningMessages;
            var errorMessages = result.First().GetMessages().ErrorMessages;

            Assert.NotNull(result);
        }

        private RulesEngine GetRulesEngine(string filename)
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "TestData", filename);
            var data = File.ReadAllText(filePath);

            var injectWorkflow = new WorkflowRules
            {
                WorkflowName = "inputWorkflowReference",
                WorkflowRulesToInject = new List<string> { "inputWorkflow" }
            };

            var injectWorkflowStr = JsonConvert.SerializeObject(injectWorkflow);
            var mockLogger = new Mock<ILogger>();
            return new RulesEngine(new[] { data, injectWorkflowStr }, mockLogger.Object);
        }

        private dynamic GetInput1()
        {
            var person = new Person
            {
                FirstName = "Japan",
                LastName = "User",
                Age = 20,
                Email = "user21@email.com",
                Salary = 100,
                Sex = "M",
                Addresses = new List<Address>()
                {
                    new Address
                    {
                        Address1 = "123 State St",
                        City = "Harrisburg",
                        State = "PA",
                        ZipCode = "17050",
                        Country = "USA"
                    }
                }
            };

            return person;
        }
    }
}