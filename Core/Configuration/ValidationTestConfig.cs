using System;
using Microsoft.Extensions.Configuration;

namespace FastApi_NetCore.Core.Configuration
{
    /// <summary>
    /// Configuration for enabling/disabling validation test handlers
    /// </summary>
    public static class ValidationTestConfig
    {
        private static bool? _enableValidationTests;

        /// <summary>
        /// Determines if validation test handlers should be active
        /// Can be controlled via:
        /// 1. Environment variable: FASTAPI_ENABLE_VALIDATION_TESTS=true/false
        /// 2. App setting: EnableValidationTests=true/false
        /// 3. Default: false (disabled in production)
        /// </summary>
        public static bool EnableValidationTests
        {
            get
            {
                if (!_enableValidationTests.HasValue)
                {
                    // Check environment variable first
                    var envVar = Environment.GetEnvironmentVariable("FASTAPI_ENABLE_VALIDATION_TESTS");
                    if (!string.IsNullOrEmpty(envVar))
                    {
                        _enableValidationTests = bool.TryParse(envVar, out bool envResult) && envResult;
                        return _enableValidationTests.Value;
                    }

                    // Simple fallback without complex configuration loading
                    // In production scenarios, this would be injected via DI
                    // For now, we'll rely on environment variable or explicit control

                    // Default to disabled
                    _enableValidationTests = false;
                }

                return _enableValidationTests.Value;
            }
        }

        /// <summary>
        /// Force enable validation tests (for development/testing)
        /// </summary>
        public static void ForceEnable()
        {
            _enableValidationTests = true;
        }

        /// <summary>
        /// Force disable validation tests (for production)
        /// </summary>
        public static void ForceDisable()
        {
            _enableValidationTests = false;
        }
    }
}