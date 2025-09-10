using FastApi_NetCore.Core.Attributes;
using FastApi_NetCore.Core.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace FastApi_NetCore.Handlers.System
{
    /// <summary>
    /// Administrative System Operations
    /// SECURITY POLICY: JWT + Admin role required GLOBALLY, restrictive rate limiting
    /// </summary>
    [Authorize(Type = AuthorizationType.JWT, Roles = "Admin")] // GLOBAL: JWT + Admin for ALL methods
    [RateLimit(30, 600)]                                       // GLOBAL: 30 admin operations per 10 minutes
    internal class AdminSystemHandlers
    {
        [RouteConfiguration("/admin/system/detailed-health", HttpMethodType.GET)]
        internal async Task DetailedHealthCheck(HttpListenerContext context)
        {
            var process = Process.GetCurrentProcess();
            
            var detailedHealth = new
            {
                Message = "🔍 Detailed System Health (Admin Only)",
                Status = "Healthy",
                Service = "FastApi NetCore - Administrative Health Check",
                Timestamp = DateTime.UtcNow,
                
                SystemInfo = new
                {
                    MachineName = Environment.MachineName,
                    OSVersion = Environment.OSVersion.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    Is64BitOS = Environment.Is64BitOperatingSystem,
                    WorkingDirectory = Environment.CurrentDirectory,
                    UserDomainName = Environment.UserDomainName,
                    SystemPageSize = Environment.SystemPageSize
                },
                
                ProcessInfo = new
                {
                    ProcessId = process.Id,
                    ProcessName = process.ProcessName,
                    StartTime = process.StartTime,
                    WorkingSetMB = process.WorkingSet64 / (1024 * 1024),
                    PrivateMemoryMB = process.PrivateMemorySize64 / (1024 * 1024),
                    ThreadCount = process.Threads.Count,
                    HandleCount = process.HandleCount,
                    PagedMemoryMB = process.PagedMemorySize64 / (1024 * 1024)
                },
                
                MemoryInfo = new
                {
                    TotalMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024),
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2),
                    TotalAllocatedBytes = GC.GetTotalAllocatedBytes()
                },
                
                Security = new
                {
                    AuthRequired = "JWT + Admin Role (GLOBAL policy)",
                    RateLimit = "30 operations per 10 minutes (GLOBAL policy)",
                    SensitiveData = "System diagnostics - Admin only",
                    AccessLevel = "System Administrators Only"
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, detailedHealth, true);
        }

        [RouteConfiguration("/admin/system/gc-collect", HttpMethodType.POST)]
        internal async Task ForceGarbageCollection(HttpListenerContext context)
        {
            var beforeGC = GC.GetTotalMemory(false);
            var sw = Stopwatch.StartNew();
            
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            
            sw.Stop();
            var afterGC = GC.GetTotalMemory(false);
            
            var response = new
            {
                Message = "🗑️ Garbage Collection Forced (Admin Operation)",
                Description = "Manual garbage collection triggered - Admin only operation",
                Operation = new
                {
                    Action = "FORCE_GC",
                    Status = "COMPLETED",
                    ExecutionTimeMs = sw.ElapsedMilliseconds,
                    Timestamp = DateTime.UtcNow
                },
                MemoryStats = new
                {
                    BeforeGC_MB = beforeGC / (1024 * 1024),
                    AfterGC_MB = afterGC / (1024 * 1024),
                    FreedMemory_MB = (beforeGC - afterGC) / (1024 * 1024),
                    Gen0Collections = GC.CollectionCount(0),
                    Gen1Collections = GC.CollectionCount(1),
                    Gen2Collections = GC.CollectionCount(2)
                },
                Security = new
                {
                    AuthRequired = "JWT + Admin Role (GLOBAL policy)",
                    RateLimit = "30 operations per 10 minutes (GLOBAL policy)",
                    Warning = "Memory operations can impact performance",
                    AccessLevel = "System Administrators Only"
                }
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/admin/system/environment", HttpMethodType.GET)]
        internal async Task GetEnvironmentInfo(HttpListenerContext context)
        {
            var envVars = Environment.GetEnvironmentVariables();
            var filteredEnvVars = new Dictionary<string, string>();
            
            // Filter sensitive environment variables
            foreach (DictionaryEntry envVar in envVars)
            {
                var key = envVar.Key?.ToString() ?? "";
                var value = envVar.Value?.ToString() ?? "";
                
                // Hide sensitive information
                if (key.ToUpper().Contains("PASSWORD") || 
                    key.ToUpper().Contains("SECRET") || 
                    key.ToUpper().Contains("KEY") ||
                    key.ToUpper().Contains("TOKEN"))
                {
                    filteredEnvVars[key] = "[HIDDEN]";
                }
                else
                {
                    filteredEnvVars[key] = value;
                }
            }
            
            var response = new
            {
                Message = "🌍 System Environment Information (Admin Only)",
                Description = "Complete system environment details - Admin only access",
                SystemEnvironment = new
                {
                    MachineName = Environment.MachineName,
                    OSVersion = Environment.OSVersion,
                    CLRVersion = Environment.Version,
                    ProcessorCount = Environment.ProcessorCount,
                    Is64BitOperatingSystem = Environment.Is64BitOperatingSystem,
                    Is64BitProcess = Environment.Is64BitProcess,
                    UserName = Environment.UserName,
                    UserDomainName = Environment.UserDomainName,
                    CurrentDirectory = Environment.CurrentDirectory,
                    SystemDirectory = Environment.SystemDirectory,
                    CommandLine = Environment.CommandLine
                },
                EnvironmentVariables = filteredEnvVars,
                Security = new
                {
                    AuthRequired = "JWT + Admin Role (GLOBAL policy)",
                    RateLimit = "30 operations per 10 minutes (GLOBAL policy)",
                    DataSensitivity = "Environment variables filtered for security",
                    AccessLevel = "System Administrators Only",
                    Note = "Sensitive values (passwords, secrets, keys) are hidden"
                },
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }

        [RouteConfiguration("/admin/system/process-info", HttpMethodType.GET)]
        internal async Task GetProcessInformation(HttpListenerContext context)
        {
            var currentProcess = Process.GetCurrentProcess();
            var allProcesses = Process.GetProcesses();
            
            var response = new
            {
                Message = "⚙️ Process Information (Admin Only)",
                Description = "Detailed process information - Admin only access",
                CurrentProcess = new
                {
                    Id = currentProcess.Id,
                    Name = currentProcess.ProcessName,
                    StartTime = currentProcess.StartTime,
                    WorkingSet64 = currentProcess.WorkingSet64,
                    PrivateMemorySize64 = currentProcess.PrivateMemorySize64,
                    ThreadCount = currentProcess.Threads.Count,
                    HandleCount = currentProcess.HandleCount,
                    BasePriority = currentProcess.BasePriority,
                    HasExited = currentProcess.HasExited
                },
                SystemProcesses = new
                {
                    TotalCount = allProcesses.Length,
                    RunningProcesses = allProcesses.Where(p => !p.HasExited).Count(),
                    TopMemoryConsumers = allProcesses
                        .Where(p => !p.HasExited)
                        .OrderByDescending(p => p.WorkingSet64)
                        .Take(5)
                        .Select(p => new { 
                            p.Id, 
                            p.ProcessName, 
                            WorkingSetMB = p.WorkingSet64 / (1024 * 1024) 
                        })
                        .ToArray()
                },
                Security = new
                {
                    AuthRequired = "JWT + Admin Role (GLOBAL policy)",
                    RateLimit = "30 operations per 10 minutes (GLOBAL policy)",
                    DataSensitivity = "Process information - System administrative data",
                    AccessLevel = "System Administrators Only"
                },
                Timestamp = DateTime.UtcNow
            };

            var responseHandler = context.GetService<IHttpResponseHandler>();
            await responseHandler.SendAsync(context, response, true);
        }
    }
}