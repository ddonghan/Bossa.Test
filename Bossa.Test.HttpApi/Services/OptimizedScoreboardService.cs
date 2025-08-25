using Bossa.Test.HttpApi.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Bossa.Test.HttpApi.Services
{
    /// <summary>
    /// Optimized skip list based scoreboard service with lock striping
    /// </summary>
    public class OptimizedScoreboardService : IScoreboardService
    {
        // Stores customer scores
        private readonly ConcurrentDictionary<long, decimal> _customerScores = new();
        private readonly SkipList _skipList = new();

        // Lock striping for concurrent updates
        private const int LockCount = 4096;
        private readonly object[] _locks = new object[LockCount];

        public OptimizedScoreboardService()
        {
            for (int i = 0; i < LockCount; i++)
            {
                _locks[i] = new object();
            }
        }

        private object GetLock(long customerId)
        {
            return _locks[Math.Abs(customerId % LockCount)];
        }

        public decimal UpdateScore(long customerId, decimal scoreChange)
        {
            lock (GetLock(customerId))
            {
                if (!_customerScores.TryGetValue(customerId, out decimal currentScore))
                {
                    // New customer
                    decimal newScore = scoreChange;

                    // Only add to leaderboard if score is positive
                    if (newScore > 0)
                    {
                        _skipList.Insert(customerId, newScore);
                        _customerScores[customerId] = newScore;
                        return newScore;
                    }
                    return scoreChange; // Negative score, no insertion
                }

                // Existing customer
                decimal newScoreAfterUpdate = currentScore + scoreChange;

                // Remove if score becomes non-positive
                if (newScoreAfterUpdate <= 0)
                {
                    _skipList.Remove(customerId);
                    _customerScores.TryRemove(customerId, out _);
                    return newScoreAfterUpdate;
                }

                // Update score in skip list
                _skipList.Update(customerId, newScoreAfterUpdate);
                _customerScores[customerId] = newScoreAfterUpdate;
                return newScoreAfterUpdate;
            }
        }

        public List<CustomerRecord> GetByRank(int start, int end)
        {
            if (start < 1 || end < start) return new List<CustomerRecord>();
            return _skipList.GetRange(start, end);
        }

        public List<CustomerRecord> GetCustomerNeighbors(long customerId, int high, int low)
        {
            return _skipList.GetNeighbors(customerId, high, low);
        }
    }

    /// <summary>
    /// Optimized skip list implementation with ranking support
    /// </summary>
    public class SkipList
    {
        private class Node
        {
            public long CustomerId { get; }
            public decimal Score { get; set; }
            public Node[] Next { get; }
            public Node Prev { get; set; }  // Previous node at level 0
            public int[] Span { get; }      // Distance to next node at each level

            public Node(long customerId, decimal score, int level)
            {
                CustomerId = customerId;
                Score = score;
                Next = new Node[level];
                Span = new int[level];
                Array.Fill(Span, 1);
            }
        }

        // Configuration
        private const int MaxLevel = 32;
        private const double Probability = 0.5;
        private static readonly ThreadLocal<Random> _random = new(() => new Random());

        // Data structures
        private readonly Node _head = new(0, 0, MaxLevel);
        private int _currentLevel = 1;
        private int _count = 0;
        private readonly Dictionary<long, Node> _nodeMap = new();

        public int Count => _count;

        private int GetRandomLevel()
        {
            int level = 1;
            while (_random.Value.NextDouble() < Probability && level < MaxLevel)
            {
                level++;
            }
            return level;
        }

        // Compare two nodes: higher score first, then lower customer ID
        private int Compare(Node a, Node b)
        {
            if (a == b) return 0;
            if (a.Score != b.Score)
                return b.Score.CompareTo(a.Score);  // Higher score comes first
            return a.CustomerId.CompareTo(b.CustomerId);  // Lower customer ID comes first
        }

        /// <summary>
        /// Insert a new customer into the skip list
        /// </summary>
        public void Insert(long customerId, decimal score)
        {
            if (_nodeMap.ContainsKey(customerId))
                throw new InvalidOperationException("Customer already exists");

            Node[] update = new Node[MaxLevel];
            int[] rank = new int[MaxLevel];
            Node current = _head;

            // Initialize rank tracking
            for (int i = 0; i < MaxLevel; i++)
            {
                rank[i] = 0;
            }

            // Traverse from top level down to find insertion points
            for (int i = _currentLevel - 1; i >= 0; i--)
            {
                rank[i] = i == _currentLevel - 1 ? 0 : rank[i + 1];

                while (current.Next[i] != null &&
                       Compare(new Node(customerId, score, 1), current.Next[i]) > 0)
                {
                    rank[i] += current.Span[i];
                    current = current.Next[i];
                }
                update[i] = current;
            }

            // Create new node with random level
            int newLevel = GetRandomLevel();
            Node newNode = new Node(customerId, score, newLevel);

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

            // Insert node and update spans
            for (int i = 0; i < newLevel; i++)
            {
                newNode.Next[i] = update[i].Next[i];
                update[i].Next[i] = newNode;

                // Calculate and update spans
                newNode.Span[i] = update[i].Span[i] - (rank[0] - rank[i]);
                update[i].Span[i] = rank[0] - rank[i] + 1;
            }

            // Update spans for unaffected higher levels
            for (int i = newLevel; i < _currentLevel; i++)
            {
                update[i].Span[i]++;
            }

            // Update level 0 prev pointers (doubly-linked list)
            newNode.Prev = update[0] == _head ? null : update[0];
            if (newNode.Next[0] != null)
            {
                newNode.Next[0].Prev = newNode;
            }

            _count++;
            _nodeMap[customerId] = newNode;
        }

        /// <summary>
        /// Update existing customer score
        /// </summary>
        public void Update(long customerId, decimal newScore)
        {
            if (!_nodeMap.TryGetValue(customerId, out Node node))
                throw new KeyNotFoundException("Customer not found");

            // Check if position doesn't change
            if (node.Prev != null && Compare(node.Prev, node) < 0 &&
                node.Next[0] != null && Compare(node, node.Next[0]) < 0)
            {
                node.Score = newScore;
                return;
            }

            // Full remove and reinsert if position changes
            Remove(node);
            Insert(customerId, newScore);
        }

        // Internal remove without dictionary cleanup
        private void Remove(Node node)
        {
            Node[] update = new Node[MaxLevel];
            Node current = _head;

            // Find update points
            for (int i = _currentLevel - 1; i >= 0; i--)
            {
                while (current.Next[i] != null && Compare(current.Next[i], node) < 0)
                {
                    current = current.Next[i];
                }
                update[i] = current;
            }

            // Verify we found the node
            if (current.Next[0] != node) return;

            // Remove node and update spans
            for (int i = 0; i < _currentLevel; i++)
            {
                if (update[i].Next[i] == node)
                {
                    update[i].Span[i] += node.Span[i] - 1;
                    update[i].Next[i] = node.Next[i];
                }
                else
                {
                    update[i].Span[i]--;
                }
            }

            // Update prev pointers
            if (node.Next[0] != null)
            {
                node.Next[0].Prev = node.Prev;
            }
            if (node.Prev != null)
            {
                node.Prev.Next[0] = node.Next[0];
            }

            // Clean up top levels
            while (_currentLevel > 1 && _head.Next[_currentLevel - 1] == null)
            {
                _currentLevel--;
            }

            _count--;
            _nodeMap.Remove(node.CustomerId);
        }

        public bool Remove(long customerId)
        {
            if (!_nodeMap.TryGetValue(customerId, out Node node))
                return false;

            Remove(node);
            return true;
        }

        public List<CustomerRecord> GetRange(int start, int end)
        {
            if (start < 1 || end < start || start > _count)
                return new List<CustomerRecord>();

            end = Math.Min(end, _count);
            var results = new List<CustomerRecord>(end - start + 1);

            int currentRank = 0;
            Node current = _head;

            // Fast forward to start position using skip list
            for (int i = _currentLevel - 1; i >= 0; i--)
            {
                while (current.Next[i] != null && currentRank + current.Span[i] <= start)
                {
                    currentRank += current.Span[i];
                    current = current.Next[i];
                }
            }

            // Linear traversal for the remaining nodes
            while (currentRank < start)
            {
                current = current.Next[0];
                currentRank++;
            }

            // Collect results in descending score order
            for (int rank = start; rank <= end; rank++)
            {
                results.Add(new CustomerRecord
                {
                    CustomerId = current.CustomerId,
                    Score = current.Score,
                    Rank = rank
                });

                current = current.Next[0];
                if (current == null) break;
            }

            return results;
        }

        public List<CustomerRecord> GetNeighbors(long customerId, int high, int low)
        {
            if (!_nodeMap.TryGetValue(customerId, out Node node))
                return new List<CustomerRecord>();

            // Calculate rank of target node
            int rank = 1;
            Node current = _head;
            for (int i = _currentLevel - 1; i >= 0; i--)
            {
                while (current.Next[i] != null)
                {
                    if (Compare(current.Next[i], node) <= 0)
                    {
                        rank += current.Span[i];
                        current = current.Next[i];
                    }
                    else
                    {
                        break;
                    }
                }
            }

            var neighbors = new List<CustomerRecord>();

            // Collect higher neighbors (before target)
            Node prev = node.Prev;
            for (int i = 0; i < high && prev != null; i++)
            {
                neighbors.Insert(0, new CustomerRecord
                {
                    CustomerId = prev.CustomerId,
                    Score = prev.Score,
                    Rank = rank - (high - i)
                });
                prev = prev.Prev;
            }

            // Add target node
            neighbors.Add(new CustomerRecord
            {
                CustomerId = node.CustomerId,
                Score = node.Score,
                Rank = rank
            });

            // Collect lower neighbors (after target)
            Node next = node.Next[0];
            for (int i = 1; i <= low && next != null; i++)
            {
                neighbors.Add(new CustomerRecord
                {
                    CustomerId = next.CustomerId,
                    Score = next.Score,
                    Rank = rank + i
                });
                next = next.Next[0];
            }

            return neighbors;
        }
    }
}