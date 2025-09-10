using System;

namespace FastApi_NetCore.Core.Attributes
{
    /// <summary>
    /// Marks a handler as a validation test handler
    /// These handlers are only active when ValidationTestConfig.EnableValidationTests is true
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    internal class ValidationTestAttribute : Attribute
    {
        /// <summary>
        /// Description of what this test handler validates
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Test case identifier
        /// </summary>
        public string TestCase { get; set; }

        public ValidationTestAttribute(string testCase = "", string description = "")
        {
            TestCase = testCase;
            Description = description;
        }
    }
}