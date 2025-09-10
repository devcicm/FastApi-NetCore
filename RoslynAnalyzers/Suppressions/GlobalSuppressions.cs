// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// Suppress analyzer warnings for test files
[assembly: SuppressMessage("Security", "FAPI001:Global Policy Violation: Duplicate Authorization Attribute", Justification = "Test files may intentionally violate policies for testing purposes", Scope = "type", Target = "~T:FastApi_NetCore.TestConflictHandler")]
[assembly: SuppressMessage("Security", "FAPI002:Global Policy Violation: Duplicate RateLimit Attribute", Justification = "Test files may intentionally violate policies for testing purposes", Scope = "type", Target = "~T:FastApi_NetCore.TestConflictHandler")]