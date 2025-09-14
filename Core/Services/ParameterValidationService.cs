using System;
using System.Collections.Generic;
using System.Linq;

namespace FastApi_NetCore.Core.Services
{
    /// <summary>
    /// Centralized parameter validation service
    /// Eliminates validation logic duplication across handlers
    /// </summary>
    public static class ParameterValidationService
    {
        /// <summary>
        /// Centralized integer validation with range checking
        /// Used by delay, status code, pagination, etc.
        /// </summary>
        public static ValidationResult<int> ValidateIntRange(string value, int min, int max, 
            int defaultValue, string paramName)
        {
            if (string.IsNullOrEmpty(value))
                return ValidationResult<int>.Success(defaultValue, $"Using default value {defaultValue}");
                
            if (!int.TryParse(value, out var parsed))
                return ValidationResult<int>.CreateError($"Invalid {paramName}: '{value}' is not a valid number");
                
            if (parsed < min || parsed > max)
                return ValidationResult<int>.CreateError($"Invalid {paramName}: {parsed} must be between {min} and {max}");
                
            return ValidationResult<int>.Success(parsed, $"Valid {paramName}: {parsed}");
        }

        /// <summary>
        /// Delay seconds validation - specific business rules
        /// </summary>
        public static ValidationResult<int> ValidateDelaySeconds(string value)
        {
            return ValidateIntRange(value, 0, 30, 2, "delay seconds");
        }

        /// <summary>
        /// HTTP status code validation - specific business rules
        /// </summary>
        public static ValidationResult<int> ValidateStatusCode(string value)
        {
            return ValidateIntRange(value, 100, 599, 418, "HTTP status code");
        }

        /// <summary>
        /// User ID validation - specific business rules
        /// </summary>
        public static ValidationResult<int> ValidateUserId(string value)
        {
            return ValidateIntRange(value, 1, int.MaxValue, 0, "user ID");
        }

        /// <summary>
        /// Page size validation for pagination
        /// </summary>
        public static ValidationResult<int> ValidatePageSize(string value)
        {
            return ValidateIntRange(value, 1, 100, 10, "page size");
        }

        /// <summary>
        /// String validation with length constraints
        /// </summary>
        public static ValidationResult<string> ValidateString(string value, int minLength, 
            int maxLength, bool allowEmpty, string paramName)
        {
            if (string.IsNullOrEmpty(value))
            {
                if (allowEmpty)
                    return ValidationResult<string>.Success(value ?? "", "Empty value allowed");
                else
                    return ValidationResult<string>.CreateError($"{paramName} cannot be empty");
            }

            if (value.Length < minLength)
                return ValidationResult<string>.CreateError($"{paramName} must be at least {minLength} characters long");

            if (value.Length > maxLength)
                return ValidationResult<string>.CreateError($"{paramName} must be no more than {maxLength} characters long");

            return ValidationResult<string>.Success(value, $"Valid {paramName}");
        }

        /// <summary>
        /// Email validation
        /// </summary>
        public static ValidationResult<string> ValidateEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ValidationResult<string>.CreateError("Email is required");

            try
            {
                var addr = new System.Net.Mail.MailAddress(value);
                return addr.Address == value 
                    ? ValidationResult<string>.Success(value, "Valid email format")
                    : ValidationResult<string>.CreateError("Invalid email format");
            }
            catch
            {
                return ValidationResult<string>.CreateError("Invalid email format");
            }
        }

        /// <summary>
        /// GUID validation
        /// </summary>
        public static ValidationResult<Guid> ValidateGuid(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return ValidationResult<Guid>.CreateError($"{paramName} is required");

            if (Guid.TryParse(value, out var guid))
                return ValidationResult<Guid>.Success(guid, $"Valid {paramName}");
            else
                return ValidationResult<Guid>.CreateError($"Invalid {paramName} format");
        }

        /// <summary>
        /// Enum validation
        /// </summary>
        public static ValidationResult<T> ValidateEnum<T>(string value, string paramName) where T : struct, Enum
        {
            if (string.IsNullOrWhiteSpace(value))
                return ValidationResult<T>.CreateError($"{paramName} is required");

            if (Enum.TryParse<T>(value, true, out var enumValue))
                return ValidationResult<T>.Success(enumValue, $"Valid {paramName}: {enumValue}");
            else
            {
                var validValues = string.Join(", ", Enum.GetNames<T>());
                return ValidationResult<T>.CreateError($"Invalid {paramName}. Valid values are: {validValues}");
            }
        }

        /// <summary>
        /// Multiple validation runner - validates multiple parameters at once
        /// </summary>
        public static MultiValidationResult ValidateMultiple(params (string name, Func<ValidationResult> validator)[] validations)
        {
            var results = new List<ValidationResult>();
            var errors = new List<string>();
            var messages = new List<string>();

            foreach (var (name, validator) in validations)
            {
                try
                {
                    var result = validator();
                    results.Add(result);
                    
                    if (result.IsValid)
                        messages.Add(result.Message);
                    else
                        errors.Add($"{name}: {result.Error}");
                }
                catch (Exception ex)
                {
                    errors.Add($"{name}: Validation error - {ex.Message}");
                }
            }

            return new MultiValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors,
                Messages = messages,
                ValidationCount = validations.Length
            };
        }
    }

    /// <summary>
    /// Generic validation result
    /// </summary>
    public class ValidationResult<T>
    {
        public bool IsValid { get; set; }
        public string Error { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public T Value { get; set; }

        public static ValidationResult<T> Success(T value, string message = "Validation successful")
        {
            return new ValidationResult<T>
            {
                IsValid = true,
                Value = value,
                Message = message
            };
        }

        public static ValidationResult<T> CreateError(string error)
        {
            return new ValidationResult<T>
            {
                IsValid = false,
                Error = error,
                Value = default
            };
        }
    }

    /// <summary>
    /// Base validation result
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Error { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Multiple validation result
    /// </summary>
    public class MultiValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Messages { get; set; } = new List<string>();
        public int ValidationCount { get; set; }

        public string GetErrorSummary() => string.Join("; ", Errors);
        public string GetMessageSummary() => string.Join("; ", Messages);
    }
}