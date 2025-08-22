using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bossa.Test.HttpApi.Models;

namespace Bossa.Test.HttpApi.Services
{
    /// <summary>
    /// High-performance in-memory leaderboard service with lock-free reads and partitioned writes
    /// </summary>
    public class LeaderboardService
    {
        // Atomic score storage with thread-safe updates
        private readonly ConcurrentDictionary<long, decimal> _scores = new();
        // Immutable leaderboard snapshot (lock-free reads)
        private ImmutableSortedSet<(long CustomerId, decimal Score)> _leaderboard =
            ImmutableSortedSet<(long, decimal)>.Empty.WithComparer(new LeaderboardComparer());
        // Partitioned locks for write operations
        private readonly object[] _partitionLocks;
        // Background update synchronization
        private readonly ReaderWriterLockSlim _leaderboardLock = new();
        private readonly Timer _updateTimer;
        private volatile bool _updateRequired;
        public LeaderboardService()
        {
            // Initialize partitioned locks (non-null guarantee)
            _partitionLocks = new object[Environment.ProcessorCount * 4];
            for (int i = 0; i < _partitionLocks.Length; i++)
            {
                _partitionLocks[i] = new object();
            }

            // Setup background leaderboard update timer (500ms interval)
            _updateTimer = new Timer(_ => UpdateLeaderboardSnapshot(), null, 500, 500);
        }
        /// <summary>
        /// Update customer score with atomic guarantees
        /// </summary>
        public async Task<decimal> UpdateScoreAsync(long customerId, decimal delta)
        {
            await Task.Yield();//Simulate asynchronous operation
            // Select partition lock based on customer ID
            var locker = _partitionLocks[Math.Abs(customerId % _partitionLocks.Length)];
            lock (locker)
            {
                // Calculate new score
                var newScore = _scores.AddOrUpdate(customerId, delta, (_, old) => old + delta);

                // Handle negative scores
                if (newScore <= 0)
                {
                    _scores.TryRemove(customerId, out _);
                }
                // Mark leaderboard for update
                _updateRequired = true;
                return newScore;
            }
        }
        /// <summary>
        /// Update leaderboard snapshot in background
        /// </summary>
        private void UpdateLeaderboardSnapshot()
        {
            if (!_updateRequired) return;
            try
            {
                _leaderboardLock.EnterWriteLock();
                _updateRequired = false;
                // Create new immutable snapshot
                var builder = _leaderboard.ToBuilder();
                builder.Clear();
                foreach (var kvp in _scores)
                {
                    builder.Add((kvp.Key, kvp.Value));
                }
                _leaderboard = builder.ToImmutable();
            }
            finally
            {
                _leaderboardLock.ExitWriteLock();
            }
        }
        /// <summary>
        /// Get leaderboard rankings by position range
        /// </summary>
        public async Task<IEnumerable<CustomerRank>> GetByRankRangeAsync(int start, int end)
        {
            await Task.Yield();//Simulate asynchronous operation
            try
            {
                _leaderboardLock.EnterReadLock();

                return _leaderboard
                    .Select((entry, index) => new CustomerRank(
                        entry.CustomerId,
                        entry.Score,
                        Rank: index + 1))
                    .Where(r => r.Rank >= start && r.Rank <= end)
                    .ToArray();
            }
            finally
            {
                _leaderboardLock.ExitReadLock();
            }
        }
        /// <summary>
        /// Get rankings around a specific customer
        /// </summary>
        public async Task<IEnumerable<CustomerRank>> GetNeighborRangeAsync(
            long customerId, int high = 0, int low = 0)
        {
            await Task.Yield();//Simulate asynchronous operation
            try
            {
                _leaderboardLock.EnterReadLock();
                // Find target position
                var targetIndex = -1;
                var items = _leaderboard.ToArray();

                for (int i = 0; i < items.Length; i++)
                {
                    if (items[i].CustomerId == customerId)
                    {
                        targetIndex = i;
                        break;
                    }
                }
                if (targetIndex == -1)
                    return Array.Empty<CustomerRank>();
                // Calculate neighbor range
                var startIndex = Math.Max(0, targetIndex - high);
                var endIndex = Math.Min(items.Length - 1, targetIndex + low);
                return items
                    .Skip(startIndex)
                    .Take(endIndex - startIndex + 1)
                    .Select((entry, index) => new CustomerRank(
                        entry.CustomerId,
                        entry.Score,
                        Rank: startIndex + index + 1));
            }
            finally
            {
                _leaderboardLock.ExitReadLock();
            }
        }
        /// <summary>
        /// Cleanup resources
        /// </summary>
        public void Dispose()
        {
            _updateTimer?.Dispose();
            _leaderboardLock?.Dispose();
        }
    }
    /// <summary>
    /// Custom comparer for leaderboard ordering
    /// </summary>
    public class LeaderboardComparer : IComparer<(long CustomerId, decimal Score)>
    {
        public int Compare((long CustomerId, decimal Score) x, (long CustomerId, decimal Score) y)
        {
            int scoreCompare = y.Score.CompareTo(x.Score); // Descending by score
            return scoreCompare != 0 ? scoreCompare : x.CustomerId.CompareTo(y.CustomerId); // Ascending by ID
        }
    }
}