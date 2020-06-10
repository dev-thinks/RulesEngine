using System.Collections.Generic;

namespace RulesEngine.UnitTest.Models
{
    public class Person
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public int Age { get; set; }

        public string Email { get; set; }

        public decimal Salary { get; set; }

        public string Sex { get; set; }

        public List<Address> Addresses { get; set; }
    }
}
