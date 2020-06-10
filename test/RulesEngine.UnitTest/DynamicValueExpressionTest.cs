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
using System.Threading.Tasks;
using Xunit;

namespace RulesEngine.UnitTest
{
    [Trait("Category", "Unit")]
    public class DynamicValueExpressionTest
    {
        [Theory]
        [InlineData("rules5.json")]
        public async Task ExecuteRule_ReturnsListOfRuleResultTree_ResultMessage(string ruleFileName)
        {
            var re = GetRulesEngine(ruleFileName);

            Person personModel = GetPersonModel();

            var result = await re.ResolveAndExecuteRule("personValidation", personModel);

            var isFailed = result.Any(s => !s.IsSuccess);
            var allMessages = result.Select(s => s.GetMessages()).ToList();

            var warningMessages = allMessages.SelectMany(s => s.WarningMessages).ToList();
            var errorMessages = allMessages.SelectMany(s => s.ErrorMessages).ToList();
            var successRules = allMessages.SelectMany(s => s.SuccessRuleNames).ToList();

            Assert.NotNull(result);
        }

        private RulesEngine GetRulesEngine(string filename)
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "TestData", filename);
            var data = File.ReadAllText(filePath);

            var mockLogger = new Mock<ILogger>();
            return new RulesEngine(new[] {data}, mockLogger.Object);
        }

        private dynamic GetPersonModel()
        {
            var person = new Person
            {
                FirstName = "Japan",
                LastName = "User",
                Age = 20,
                Email = "no-reply@acclaimsystems.onmicrosoft.com",
                Salary = 100,
                Sex = "M",
                Addresses = new List<Address>()
                {
                    new Address
                    {
                        Address1 = "123 State St",
                        City = "Harrisburg",
                        State = "no-reply@acclaimsystems.onmicrosoft.com",
                        ZipCode = "17050",
                        Country = "USA"
                    }
                }
            };

            return person;
        }
    }
}
