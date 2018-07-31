﻿using System;
using System.Threading;

namespace StackExchange.Redis
{
    internal static class CompletionManagerHelpers
    {
        public static void CompleteSyncOrAsync(this PhysicalBridge bridge, ICompletable operation)
            => CompletionManager.CompleteSyncOrAsyncImpl(bridge?.completionManager, operation);
        public static void CompleteSyncOrAsync(this CompletionManager manager, ICompletable operation)
            => CompletionManager.CompleteSyncOrAsyncImpl(manager, operation);
    }
    internal sealed partial class CompletionManager
    {

        internal static void CompleteSyncOrAsyncImpl(CompletionManager manager, ICompletable operation)
        {
            if (manager != null) manager.PerInstanceCompleteSyncOrAsync(operation);
            else SharedCompleteSyncOrAsync(operation);
        }

        private readonly ConnectionMultiplexer multiplexer;

        private readonly string name;

        private long completedSync, completedAsync, failedAsync;
        public CompletionManager(ConnectionMultiplexer multiplexer, string name)
        {
            this.multiplexer = multiplexer;
            this.name = name;
        }

        private static void SharedCompleteSyncOrAsync(ICompletable operation)
        {
            if (operation == null) return;
            if (!operation.TryComplete(false))
            {
                SocketManager.Shared.ScheduleTask(s_AnyOrderCompletionHandler, operation);
            }
        }
        private void PerInstanceCompleteSyncOrAsync(ICompletable operation)
        {
            if (operation == null) return;
            if (operation.TryComplete(false))
            {
                multiplexer.Trace("Completed synchronously: " + operation, name);
                Interlocked.Increment(ref completedSync);
            }
            else
            {
                multiplexer.Trace("Using thread-pool for asynchronous completion", name);
                multiplexer.SocketManager.ScheduleTask(s_AnyOrderCompletionHandler, operation);
                Interlocked.Increment(ref completedAsync); // k, *technically* we haven't actually completed this yet, but: close enough
            }
        }

        internal void GetCounters(ConnectionCounters counters)
        {
            counters.CompletedSynchronously = Interlocked.Read(ref completedSync);
            counters.CompletedAsynchronously = Interlocked.Read(ref completedAsync);
            counters.FailedAsynchronously = Interlocked.Read(ref failedAsync);
        }

        private static readonly Action<object> s_AnyOrderCompletionHandler = AnyOrderCompletionHandler;
        private static void AnyOrderCompletionHandler(object state)
        {
            try
            {
                ConnectionMultiplexer.TraceWithoutContext("Completing async (any order): " + state);
                ((ICompletable)state).TryComplete(true);
            }
            catch (Exception ex)
            {
                ConnectionMultiplexer.TraceWithoutContext("Async completion error: " + ex.Message);
            }
        }
    }
}
