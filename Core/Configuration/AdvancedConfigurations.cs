using System;
using System.Collections.Generic;

namespace FastApi_NetCore.Core.Configuration
{
    // ===================================
    // 📦 1. PARTITIONING CONFIGURATION
    // ===================================
    public class PartitioningConfig
    {
        public int BasePartitions { get; set; } = 0; // 0 = Auto-detect
        public int MinPartitions { get; set; } = 4;
        public int MaxPartitions { get; set; } = 32;
        public int MaxQueueDepthPerPartition { get; set; } = 2000;
        public int PartitionBufferSize { get; set; } = 1000;
        public int RequestTimeoutSeconds { get; set; } = 15;
        public int ProcessingTimeoutSeconds { get; set; } = 30;
        public int ShutdownTimeoutSeconds { get; set; } = 10;
        public int PriorityLevels { get; set; } = 3;
        public bool EnablePriorityFallback { get; set; } = true;
        public Dictionary<string, int> PriorityTimeouts { get; set; } = new()
        {
            ["High"] = 5,
            ["Normal"] = 15,
            ["Low"] = 30
        };
        public bool EnableProcessingLogs { get; set; } = false;
        public bool EnableDetailedMetrics { get; set; } = true;
        public bool LogPartitionDistribution { get; set; } = true;
        public int StatisticsReportingIntervalSeconds { get; set; } = 60;
        public bool EnableSessionAffinity { get; set; } = true;
        public string[] SessionAffinityMethods { get; set; } = { "Authorization", "SessionId", "ClientIP" };
        public int SessionExpirationMinutes { get; set; } = 30;
    }

    // ===================================
    // ⚖️ 2. LOAD BALANCING CONFIGURATION
    // ===================================
    public class LoadBalancingConfig
    {
        public string PrimaryStrategy { get; set; } = "LeastBusy";
        public string FallbackStrategy { get; set; } = "RoundRobin";
        public bool EmergencyFallbackEnabled { get; set; } = true;
        public int WorkerHealthCheckIntervalSeconds { get; set; } = 30;
        public int HealthCheckTimeoutSeconds { get; set; } = 5;
        public int UnhealthyWorkerRetryIntervalSeconds { get; set; } = 60;
        public int CapacityThresholdPercentage { get; set; } = 80;
        public int OverloadThresholdPercentage { get; set; } = 95;
        public int LoadBalancingWindowSeconds { get; set; } = 10;
        public CircuitBreakerConfig CircuitBreaker { get; set; } = new();
        public WorkerSelectionConfig WorkerSelection { get; set; } = new();
        public bool EnableLoadBalancingMetrics { get; set; } = true;
        public bool TrackWorkerPerformance { get; set; } = true;
        public bool LogLoadBalancingDecisions { get; set; } = false;
        public int MetricsRetentionMinutes { get; set; } = 60;
    }

    public class CircuitBreakerConfig
    {
        public int ErrorThresholdPercentage { get; set; } = 25;
        public int MinimumRequestsInPeriod { get; set; } = 10;
        public int CircuitOpenDurationSeconds { get; set; } = 30;
        public int HalfOpenMaxRequests { get; set; } = 5;
        public bool EnableAutoRecovery { get; set; } = true;
        public int RecoveryTestIntervalSeconds { get; set; } = 15;
    }

    public class WorkerSelectionConfig
    {
        public int MaxErrorRatePercentage { get; set; } = 15;
        public bool ExcludeUnhealthyWorkers { get; set; } = true;
        public bool PreferLowLatencyWorkers { get; set; } = true;
        public bool WeightByResponseTime { get; set; } = false;
    }

    // ===================================
    // 🚀 3. CHANNELS CONFIGURATION
    // ===================================
    public class ChannelsConfig
    {
        public BoundedChannelOptionsConfig BoundedChannelOptions { get; set; } = new();
        public int[] ChannelCapacities { get; set; } = { 3000, 2000, 1000 };
        public int DefaultChannelCapacity { get; set; } = 2000;
        public int MaxChannelCapacity { get; set; } = 5000;
        public int[] BatchSizes { get; set; } = { 5, 15, 30 };
        public int WriteTimeoutMilliseconds { get; set; } = 200;
        public int ReadTimeoutMilliseconds { get; set; } = 1000;
        public int ProcessingBatchSize { get; set; } = 10;
        public bool EnableBackpressure { get; set; } = true;
        public int BackpressureThresholdPercentage { get; set; } = 90;
        public int FlowControlWindowSize { get; set; } = 100;
        public bool AdaptiveBatchSizing { get; set; } = true;
        public int MaxConcurrentReaders { get; set; } = 1;
        public int MaxConcurrentWriters { get; set; } = 0; // 0 = unlimited
        public string ThreadSafetyLevel { get; set; } = "Full";
        public bool EnableChannelMetrics { get; set; } = true;
        public bool TrackQueueDepth { get; set; } = true;
        public bool TrackProcessingLatency { get; set; } = true;
        public bool AlertOnHighQueueDepth { get; set; } = true;
        public int QueueDepthAlertThreshold { get; set; } = 1500;
        public bool EnableChannelDiagnostics { get; set; } = false;
        public int ChannelStatisticsIntervalSeconds { get; set; } = 30;
        public bool AutoAdjustCapacity { get; set; } = false;
        public int GracefulShutdownTimeoutSeconds { get; set; } = 10;
    }

    public class BoundedChannelOptionsConfig
    {
        public string FullMode { get; set; } = "Wait"; // Wait, Block, DropWrite, DropOldest
        public bool SingleReader { get; set; } = true;
        public bool SingleWriter { get; set; } = false;
        public bool AllowSynchronousContinuations { get; set; } = false;
    }

    // ===================================
    // 🎛️ PERFORMANCE PROFILES
    // ===================================
    public class PerformanceProfile
    {
        public PartitioningConfig PartitioningConfig { get; set; } = new();
        public LoadBalancingConfig LoadBalancingConfig { get; set; } = new();
        public ChannelsConfig ChannelsConfig { get; set; } = new();
    }

    public class PerformanceProfiles
    {
        public PerformanceProfile Development { get; set; } = new();
        public PerformanceProfile Testing { get; set; } = new();
        public PerformanceProfile Production { get; set; } = new();
        public PerformanceProfile HighLoad { get; set; } = new();
    }

    // ===================================
    // 🔧 CONFIGURATION EXTENSIONS
    // ===================================
    public static class ConfigurationExtensions
    {
        public static PartitioningConfig GetEffectivePartitioningConfig(
            this PartitioningConfig baseConfig,
            PerformanceProfile? profile = null)
        {
            if (profile?.PartitioningConfig == null)
                return baseConfig;

            return new PartitioningConfig
            {
                BasePartitions = profile.PartitioningConfig.BasePartitions != 0
                    ? profile.PartitioningConfig.BasePartitions
                    : baseConfig.BasePartitions,
                MaxQueueDepthPerPartition = profile.PartitioningConfig.MaxQueueDepthPerPartition != 0
                    ? profile.PartitioningConfig.MaxQueueDepthPerPartition
                    : baseConfig.MaxQueueDepthPerPartition,
                EnableProcessingLogs = profile.PartitioningConfig.EnableProcessingLogs,
                EnableDetailedMetrics = profile.PartitioningConfig.EnableDetailedMetrics,
                RequestTimeoutSeconds = profile.PartitioningConfig.RequestTimeoutSeconds != 0
                    ? profile.PartitioningConfig.RequestTimeoutSeconds
                    : baseConfig.RequestTimeoutSeconds,
                // ... copy other properties with profile overrides
                MinPartitions = baseConfig.MinPartitions,
                MaxPartitions = baseConfig.MaxPartitions,
                PartitionBufferSize = baseConfig.PartitionBufferSize,
                ProcessingTimeoutSeconds = baseConfig.ProcessingTimeoutSeconds,
                ShutdownTimeoutSeconds = baseConfig.ShutdownTimeoutSeconds,
                PriorityLevels = baseConfig.PriorityLevels,
                EnablePriorityFallback = baseConfig.EnablePriorityFallback,
                PriorityTimeouts = baseConfig.PriorityTimeouts,
                LogPartitionDistribution = baseConfig.LogPartitionDistribution,
                StatisticsReportingIntervalSeconds = baseConfig.StatisticsReportingIntervalSeconds,
                EnableSessionAffinity = baseConfig.EnableSessionAffinity,
                SessionAffinityMethods = baseConfig.SessionAffinityMethods,
                SessionExpirationMinutes = baseConfig.SessionExpirationMinutes
            };
        }

        public static LoadBalancingConfig GetEffectiveLoadBalancingConfig(
            this LoadBalancingConfig baseConfig,
            PerformanceProfile? profile = null)
        {
            if (profile?.LoadBalancingConfig == null)
                return baseConfig;

            var effectiveConfig = new LoadBalancingConfig
            {
                PrimaryStrategy = baseConfig.PrimaryStrategy,
                FallbackStrategy = baseConfig.FallbackStrategy,
                EmergencyFallbackEnabled = baseConfig.EmergencyFallbackEnabled,
                WorkerHealthCheckIntervalSeconds = profile.LoadBalancingConfig.WorkerHealthCheckIntervalSeconds != 0
                    ? profile.LoadBalancingConfig.WorkerHealthCheckIntervalSeconds
                    : baseConfig.WorkerHealthCheckIntervalSeconds,
                HealthCheckTimeoutSeconds = baseConfig.HealthCheckTimeoutSeconds,
                UnhealthyWorkerRetryIntervalSeconds = baseConfig.UnhealthyWorkerRetryIntervalSeconds,
                CapacityThresholdPercentage = profile.LoadBalancingConfig.CapacityThresholdPercentage != 0
                    ? profile.LoadBalancingConfig.CapacityThresholdPercentage
                    : baseConfig.CapacityThresholdPercentage,
                OverloadThresholdPercentage = baseConfig.OverloadThresholdPercentage,
                LoadBalancingWindowSeconds = baseConfig.LoadBalancingWindowSeconds,
                EnableLoadBalancingMetrics = profile.LoadBalancingConfig.EnableLoadBalancingMetrics,
                TrackWorkerPerformance = baseConfig.TrackWorkerPerformance,
                LogLoadBalancingDecisions = profile.LoadBalancingConfig.LogLoadBalancingDecisions,
                MetricsRetentionMinutes = baseConfig.MetricsRetentionMinutes,
                WorkerSelection = baseConfig.WorkerSelection
            };

            // Override circuit breaker settings if provided in profile
            if (profile.LoadBalancingConfig.CircuitBreaker != null)
            {
                effectiveConfig.CircuitBreaker = new CircuitBreakerConfig
                {
                    ErrorThresholdPercentage = profile.LoadBalancingConfig.CircuitBreaker.ErrorThresholdPercentage != 0
                        ? profile.LoadBalancingConfig.CircuitBreaker.ErrorThresholdPercentage
                        : baseConfig.CircuitBreaker.ErrorThresholdPercentage,
                    MinimumRequestsInPeriod = baseConfig.CircuitBreaker.MinimumRequestsInPeriod,
                    CircuitOpenDurationSeconds = baseConfig.CircuitBreaker.CircuitOpenDurationSeconds,
                    HalfOpenMaxRequests = baseConfig.CircuitBreaker.HalfOpenMaxRequests,
                    EnableAutoRecovery = baseConfig.CircuitBreaker.EnableAutoRecovery,
                    RecoveryTestIntervalSeconds = baseConfig.CircuitBreaker.RecoveryTestIntervalSeconds
                };
            }
            else
            {
                effectiveConfig.CircuitBreaker = baseConfig.CircuitBreaker;
            }

            return effectiveConfig;
        }

        public static ChannelsConfig GetEffectiveChannelsConfig(
            this ChannelsConfig baseConfig,
            PerformanceProfile? profile = null)
        {
            if (profile?.ChannelsConfig == null)
                return baseConfig;

            return new ChannelsConfig
            {
                BoundedChannelOptions = baseConfig.BoundedChannelOptions,
                ChannelCapacities = profile.ChannelsConfig.ChannelCapacities?.Length > 0
                    ? profile.ChannelsConfig.ChannelCapacities
                    : baseConfig.ChannelCapacities,
                DefaultChannelCapacity = baseConfig.DefaultChannelCapacity,
                MaxChannelCapacity = baseConfig.MaxChannelCapacity,
                BatchSizes = profile.ChannelsConfig.BatchSizes?.Length > 0
                    ? profile.ChannelsConfig.BatchSizes
                    : baseConfig.BatchSizes,
                WriteTimeoutMilliseconds = profile.ChannelsConfig.WriteTimeoutMilliseconds != 0
                    ? profile.ChannelsConfig.WriteTimeoutMilliseconds
                    : baseConfig.WriteTimeoutMilliseconds,
                ReadTimeoutMilliseconds = baseConfig.ReadTimeoutMilliseconds,
                ProcessingBatchSize = baseConfig.ProcessingBatchSize,
                EnableBackpressure = baseConfig.EnableBackpressure,
                BackpressureThresholdPercentage = baseConfig.BackpressureThresholdPercentage,
                FlowControlWindowSize = baseConfig.FlowControlWindowSize,
                AdaptiveBatchSizing = baseConfig.AdaptiveBatchSizing,
                MaxConcurrentReaders = baseConfig.MaxConcurrentReaders,
                MaxConcurrentWriters = baseConfig.MaxConcurrentWriters,
                ThreadSafetyLevel = baseConfig.ThreadSafetyLevel,
                EnableChannelMetrics = profile.ChannelsConfig.EnableChannelMetrics,
                TrackQueueDepth = baseConfig.TrackQueueDepth,
                TrackProcessingLatency = baseConfig.TrackProcessingLatency,
                AlertOnHighQueueDepth = baseConfig.AlertOnHighQueueDepth,
                QueueDepthAlertThreshold = baseConfig.QueueDepthAlertThreshold,
                EnableChannelDiagnostics = profile.ChannelsConfig.EnableChannelDiagnostics,
                ChannelStatisticsIntervalSeconds = baseConfig.ChannelStatisticsIntervalSeconds,
                AutoAdjustCapacity = baseConfig.AutoAdjustCapacity,
                GracefulShutdownTimeoutSeconds = baseConfig.GracefulShutdownTimeoutSeconds
            };
        }
    }
}