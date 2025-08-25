using Bossa.Test.HttpApi.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Bossa.Test.HttpApi.Services
{
    /// <summary>
    /// Skip list based scoreboard service implementation
    /// </summary>
    public class SkipListScoreboardService : IScoreboardService
    {
        // Stores customer scores
        private readonly ConcurrentDictionary<long, decimal> _customerScores = new();

        // Skip list for efficient ranking operations
        private readonly ConcurrentSkipList _skipList = new();

        // Number of locks for striped locking
        private const int LockCount = 4096;

        // Array of locks for fine-grained synchronization
        private readonly object[] _stripedLocks = new object[LockCount];

        public SkipListScoreboardService()
        {
            // Initialize striped locks
            for (int i = 0; i < LockCount; i++)
            {
                _stripedLocks[i] = new object();
            }
        }

        /// <summary>
        /// Gets the lock object for a specific customer ID
        /// </summary>
        private object GetLock(long customerId)
        {
            return _stripedLocks[Math.Abs(customerId % LockCount)];
        }

        /// <summary>
        /// Updates a customer's score
        /// </summary>
        public decimal UpdateScore(long customerId, decimal scoreChange)
        {
            lock (GetLock(customerId))
            {
                decimal newScore;
                bool wasInLeaderboard = false;
                decimal oldScore = 0;

                if (_customerScores.TryGetValue(customerId, out oldScore))
                {
                    // Update existing customer score
                    newScore = oldScore + scoreChange;
                    wasInLeaderboard = oldScore > 0;
                    if (newScore > 0)
                        _customerScores[customerId] = newScore;
                }
                else
                {
                    // Add new customer
                    newScore = scoreChange;
                    if (newScore > 0)
                        _customerScores.TryAdd(customerId, newScore);
                }

                // Update skip list
                if (wasInLeaderboard)
                {
                    _skipList.Remove(customerId);
                }

                if (newScore > 0)
                {
                    _skipList.Insert(customerId, newScore);
                }

                return newScore;
            }
        }

        /// <summary>
        /// Gets customers by rank range
        /// </summary>
        public List<CustomerRecord> GetByRank(int start, int end)
        {
            return _skipList.GetRange(start, end);
        }

        /// <summary>
        /// Gets neighbors around a customer's position
        /// </summary>
        public List<CustomerRecord> GetCustomerNeighbors(long customerId, int high, int low)
        {
            if (!_customerScores.TryGetValue(customerId, out decimal score))
            {
                return new List<CustomerRecord>();
            }

            if (score <= 0)
            {
                return new List<CustomerRecord>();
            }

            int? rank = _skipList.GetRank(customerId);
            if (rank == null)
            {
                return new List<CustomerRecord>();
            }

            int startRank = Math.Max(1, rank.Value - high);
            int endRank = Math.Min(_skipList.Count, rank.Value + low);

            return GetByRank(startRank, endRank);
        }
    }

    /// <summary>
    /// Thread-safe skip list implementation for leaderboard functionality
    /// </summary>
    public class ConcurrentSkipList
    {
        private class Node
        {
            public long CustomerId { get; }
            public decimal Score { get; }
            public Node[] Next { get; }
            public int[] Span { get; }

            public Node(long customerId, decimal score, int level)
            {
                CustomerId = customerId;
                Score = score;
                Next = new Node[level];
                Span = new int[level];

                // Initialize spans
                for (int i = 0; i < level; i++)
                {
                    Span[i] = 1;
                }
            }
        }

        // Maximum level for skip list nodes
        private const int MaxLevel = 32;

        // Probability for determining node levels
        private const double Probability = 0.5;

        // Head node of the skip list
        private readonly Node _head;

        // Current maximum level in use
        private int _currentLevel = 1;

        // Number of nodes in the skip list
        private int _count = 0;

        // Lock for thread safety
        private readonly ReaderWriterLockSlim _lock = new();

        // Random number generator
        private readonly Random _random = new();

        // Map for quick node lookup by customer ID
        private readonly Dictionary<long, Node> _nodeMap = new();

        public int Count => _count;
        public int MaxUsedLevel => _currentLevel;

        public ConcurrentSkipList()
        {
            _head = new Node(0, 0, MaxLevel);

            // Initialize head node spans
            for (int i = 0; i < MaxLevel; i++)
            {
                _head.Span[i] = 1;
            }
        }

        /// <summary>
        /// Generates a random level for new nodes
        /// </summary>
        private int RandomLevel()
        {
            int level = 1;
            while (_random.NextDouble() < Probability && level < MaxLevel)
            {
                level++;
            }
            return level;
        }

        /// <summary>
        /// Inserts a new customer into the skip list
        /// </summary>
        public void Insert(long customerId, decimal score)
        {
            _lock.EnterWriteLock();
            try
            {
                // Ensure customer uniqueness
                if (_nodeMap.ContainsKey(customerId))
                {
                    Remove(customerId);
                }

                Node[] update = new Node[MaxLevel];
                int[] rank = new int[MaxLevel];
                Node current = _head;

                // Initialize rank array
                for (int i = 0; i < MaxLevel; i++)
                {
                    rank[i] = 0;
                }

                // Find insertion position
                for (int i = _currentLevel - 1; i >= 0; i--)
                {
                    // Accumulate rank values
                    rank[i] = i == (_currentLevel - 1) ? 0 : rank[i + 1];

                    // Traverse current level
                    while (current.Next[i] != null)
                    {
                        // Compare scores
                        if (current.Next[i].Score > score)
                        {
                            // Higher score found, continue
                            rank[i] += current.Span[i];
                            current = current.Next[i];
                        }
                        else if (current.Next[i].Score == score)
                        {
                            // Same score, compare CustomerID
                            if (current.Next[i].CustomerId < customerId)
                            {
                                // Smaller CustomerID found, continue
                                rank[i] += current.Span[i];
                                current = current.Next[i];
                            }
                            else
                            {
                                // Found insertion position
                                break;
                            }
                        }
                        else
                        {
                            // Lower score found, stop
                            break;
                        }
                    }
                    update[i] = current;
                }

                // Determine new node level
                int newLevel = RandomLevel();

                // Expand levels if needed
                if (newLevel > _currentLevel)
                {
                    for (int i = _currentLevel; i < newLevel; i++)
                    {
                        update[i] = _head;
                        rank[i] = 0;
                        _head.Span[i] = _count + 1;
                    }
                    _currentLevel = newLevel;
                }

                // Create new node
                Node newNode = new Node(customerId, score, newLevel);

                // Insert node and update spans
                for (int i = 0; i < newLevel; i++)
                {
                    newNode.Next[i] = update[i].Next[i];
                    update[i].Next[i] = newNode;

                    // Calculate new node span
                    newNode.Span[i] = update[i].Span[i] - (rank[0] - rank[i]);

                    // Update predecessor span
                    update[i].Span[i] = rank[0] - rank[i] + 1;
                }

                // Update spans for higher levels
                for (int i = newLevel; i < _currentLevel; i++)
                {
                    update[i].Span[i]++;
                }

                _count++;
                _nodeMap[customerId] = newNode;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes a customer from the skip list
        /// </summary>
        public void Remove(long customerId)
        {
            _lock.EnterWriteLock();
            try
            {
                if (!_nodeMap.TryGetValue(customerId, out Node node))
                {
                    return;
                }

                Node[] update = new Node[MaxLevel];
                Node current = _head;

                // Locate node to remove
                for (int i = _currentLevel - 1; i >= 0; i--)
                {
                    while (current.Next[i] != null)
                    {
                        if (current.Next[i].Score > node.Score)
                        {
                            current = current.Next[i];
                        }
                        else if (current.Next[i].Score == node.Score)
                        {
                            if (current.Next[i].CustomerId < customerId)
                            {
                                current = current.Next[i];
                            }
                            else
                            {
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                    update[i] = current;
                }

                // Verify node exists
                if (current.Next[0] == null || current.Next[0].CustomerId != customerId)
                {
                    return;
                }

                // Remove node and update spans
                for (int i = 0; i < _currentLevel; i++)
                {
                    if (update[i].Next[i] == node)
                    {
                        update[i].Next[i] = node.Next[i];
                        update[i].Span[i] += node.Span[i] - 1;
                    }
                    else
                    {
                        update[i].Span[i]--;
                    }
                }

                // Adjust maximum level
                while (_currentLevel > 1 && _head.Next[_currentLevel - 1] == null)
                {
                    _currentLevel--;
                }

                _count--;
                _nodeMap.Remove(customerId);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Gets a customer's rank
        /// </summary>
        public int? GetRank(long customerId)
        {
            _lock.EnterReadLock();
            try
            {
                if (!_nodeMap.TryGetValue(customerId, out Node node))
                {
                    return null;
                }

                Node current = _head;
                int rank = 0;

                // Calculate rank
                for (int i = _currentLevel - 1; i >= 0; i--)
                {
                    while (current.Next[i] != null &&
                          (current.Next[i].Score > node.Score ||
                          (current.Next[i].Score == node.Score &&
                           current.Next[i].CustomerId < node.CustomerId)))
                    {
                        rank += current.Span[i];
                        current = current.Next[i];
                    }

                    // Check if target node found at current level
                    if (current.Next[i] != null &&
                        current.Next[i].CustomerId == customerId)
                    {
                        rank += current.Span[i];
                        return rank;
                    }
                }

                return null;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Gets a range of customers by rank
        /// </summary>
        public List<CustomerRecord> GetRange(int start, int end)
        {
            _lock.EnterReadLock();
            try
            {
                // Validate parameters
                if (start < 1 || end < start)
                {
                    return new List<CustomerRecord>();
                }

                if (start > _count)
                {
                    return new List<CustomerRecord>();
                }

                end = Math.Min(end, _count);  // Ensure end doesn't exceed count

                var results = new List<CustomerRecord>();
                int currentRank = 0;
                Node current = _head;

                // Use high-level indexes for fast positioning
                for (int i = _currentLevel - 1; i >= 0; i--)
                {
                    while (current.Next[i] != null &&
                          currentRank + current.Span[i] <= start)
                    {
                        currentRank += current.Span[i];
                        current = current.Next[i];
                    }
                }

                // Ensure we reach start position
                while (currentRank < start && current.Next[0] != null)
                {
                    current = current.Next[0];
                    currentRank++;
                }

                // Collect results
                for (int rank = start; rank <= end; rank++)
                {
                    if (current == null) break;

                    results.Add(new CustomerRecord
                    {
                        CustomerId = current.CustomerId,
                        Score = current.Score,
                        Rank = rank
                    });

                    // Move to next node
                    current = current.Next[0];
                }

                return results;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }
}