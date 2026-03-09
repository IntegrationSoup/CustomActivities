using System;

namespace HL7Soup.FakeNames
{
    public class Person
    {
        public int Number { get; set; }
        public string Gender { get; set; }
        public string Title { get; set; }
        public string GivenName { get; set; }
        public char? MiddleInitial { get; set; }
        public string Surname { get; set; }
        public string StreetAddress { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string ZipCode { get; set; }
        public string TelephoneNumber { get; set; }
        public DateTime Birthday { get; set; }
        public string NationalID { get; set; }
        public string BloodType { get; set; }
        public double Kilograms { get; set; }
        public int Centimeters { get; set; }

        // Add constructors, methods, and other properties as needed
    }
}
