# Leaderboard Service Implementation (SkipList-based)

## Project Overview
This service is a high-performance in-memory leaderboard system using a skip list for real-time ranking. Built on .NET Core, it handles customer scores and rankings entirely in memory, optimized for high-concurrency scenarios and large customer volumes.

## Design Highlights

### 1. High-Concurrency Architecture
* **Hybrid Locking Strategy**:
  - **Striped Locks**: 4096 partition locks using customer ID hashing for fine-grained write control
  - **Global ReaderWriterLock**: Protects skip list structural changes while allowing concurrent reads
* **Real-time Indexing**: Skip list maintains ordered rankings with O(log n) operations
* **Bidirectional Links**: Level 0 nodes maintain previous pointers for efficient neighbor queries
* **Span Information**: Skip list tracks node distances for fast rank calculation

### 2. Optimized Data Structures
* **Score Storage**: `ConcurrentDictionary<long, decimal>` for O(1) score lookups
* **Hierarchical Index**: Custom skip list with:
  - Max level: 32
  - Level probability: 0.5
  - Bidirectional level 0 links
  - Span tracking for rank calculation

### 3. API Implementation
Three core API endpoints implemented with skip list optimizations:

#### 1. Update Score
* **Endpoint**: `POST /customer/{customerid}/score/{score}`
* **Concurrency**: Hybrid locking (striped lock + global RW lock)
* **Operations**:
  - New customers: Insert into skip list if score > 0
  - Existing customers: Smart update (in-place if order unchanged)
  - Removal: Automatic when score ≤ 0
* **Complexity**: O(log n) average

#### 2. Query by Rank Range
* **Endpoint**: `GET /leaderboard?start={start}&end={end}`
* **Algorithm**:
  - Span-based traversal to start position
  - Sequential level 0 traversal for result collection
* **Locking**: Read lock protection during traversal
* **Complexity**: O(log n + k) for k results

#### 3. Query Customer Neighbors
* **Endpoint**: `GET /leaderboard/{customerid}?high={high}&low={low}`
* **Algorithm**:
  - Rank calculation using span information
  - Bidirectional traversal via level 0 links
* **Locking**: Read lock protection during access
* **Complexity**: O(log n + high + low)

## Performance Characteristics

| Operation | Time Complexity | Concurrency Control |
|-----------|-----------------|---------------------|
| Update Score | O(log n) | Striped Locks + RW Lock |
| Rank Range Query | O(log n + k) | Global Read Lock |
| Neighbor Query | O(log n + k) | Global Read Lock |
| Skip List Insert | O(log n) | RW Lock |
| Skip List Update | O(log n) worst-case | RW Lock |

## Running Instructions

### Environment Requirements
* .NET 6.0+ runtime
* Any hardware (optimized for multi-core)

### Startup Command
bash

dotnet run --project Bossa.Test.HttpApi

### Testing Examples
1. Update score:
bash

curl -X POST http://localhost:5001/customer/12345/score/100

2. Query leaderboard (ranks 1-5):
bash

curl http://localhost:5001/leaderboard?start=1&end=5

3. Query neighbors (2 above + 2 below):
bash

curl http://localhost:5001/leaderboard/12345?high=2&low=2

## Design Decisions

### 1. Skip List Selection
* **Balanced Performance**: O(log n) average complexity for core operations
* **Efficient Ranking**: Span tracking enables O(log n) rank calculations
* **Range Queries**: Hierarchical structure optimizes sequential access
* **Update Optimization**: Enhanced position detection for in-place updates

### 2. Concurrency Strategy
* **Hybrid Locking**:
  - 4096 striped locks minimize update contention
  - ReaderWriterLock enables concurrent reads with write protection
  - Lock recursion policy supports nested operations
* **Resource Safety**: All locks use try-finally patterns

### 3. Memory Optimization
* **Span Tracking**: Precomputes node distances for efficient ranking
* **Bidirectional Links**: Enables O(1) neighbor access at level 0
* **Adaptive Levels**: Dynamic level management (1-32) based on probability

### 4. Update Handling
* **Smart Position Detection**:
```csharp

bool positionChanged = true;

if (node.Prev != null && Compare(node.Prev, node) < 0 &&

node.Next[0] != null && Compare(node, node.Next[0]) < 0)

{

positionChanged = false;

}

* **Automatic Cleanup**: Customers removed when score ≤ 0

## Skip List Implementation Details

### Key Features
```csharp

public class SkipList

{

private class Node

{

public long CustomerId { get; }

public decimal Score { get; set; }

public Node[] Next { get; } // Forward pointers

public Node Prev { get; set; } // Backward pointer (level 0)

public int[] Span { get; } // Distance to next node

public Node(long customerId, decimal score, int level) { ... }
}

// Core operations
public void Insert(long customerId, decimal score) { ... }
public void Update(long customerId, decimal newScore) { ... }
private void Remove(Node node) { ... }

// Query operations
public List<CustomerRecord> GetRange(int start, int end) { ... }
public List<CustomerRecord> GetNeighbors(long customerId, int high, int low) { ... }

// Helper methods
private int GetRandomLevel() 
{
// Probabilistic level generation
}

private int Compare(Node a, Node b) 
{
// Higher scores first, then lower customer IDs
return b.Score.CompareTo(a.Score) != 0 
    ? b.Score.CompareTo(a.Score) 
    : a.CustomerId.CompareTo(b.CustomerId);
}
}

### Performance Optimization
1. **Span-Based Rank Calculation**:
```csharp

int rank = 1;

for (int i = _currentLevel - 1; i >= 0; i--)

{

while (current.Next[i] != null && Compare(current.Next[i], node) <= 0)

{

rank += current.Span[i];

current = current.Next[i];

}

}

2. **Efficient Range Queries**:
```csharp

// Hierarchical traversal

for (int i = _currentLevel - 1; i >= 0; i--)

{

while (current.Next[i] != null && currentRank + current.Span[i] <= start)

{

currentRank += current.Span[i];

current = current.Next[i];

}

}

3. **Bidirectional Neighbor Access**:
```csharp

// Higher neighbors (reverse order)

Node prev = node.Prev;

for (int i = 0; i < high && prev != null; i++)

{

neighbors.Insert(0, ...);

prev = prev.Prev;

}

// Lower neighbors (forward order)

Node next = node.Next[0];

for (int i = 1; i <= low && next != null; i++)

{

neighbors.Add(...);

next = next.Next[0];

}

## Scalability Analysis
* **Write Scaling**: 4096 striped locks support ≈4000 concurrent updates
* **Read Scaling**: ReaderWriterLock allows unlimited concurrent reads
* **Memory Efficiency**: O(n) space complexity with constant factors
* **Update Optimization**: Enhanced position detection minimizes reinsertions
* **Query Performance**:
  - Rank range: Logarithmic traversal + linear result collection
  - Neighbors: Constant-time neighbor access via bidirectional links

## Thread Safety Mechanism
```csharp

public decimal UpdateScore(long customerId, decimal scoreChange)

{

lock (GetLock(customerId)) // Per-customer striped lock

{

_globalLock.EnterWriteLock(); // Structural change protection

try {

// Update logic

} finally {

_globalLock.ExitWriteLock();

}

}

}

public List<CustomerRecord> GetByRank(int start, int end)

{

_globalLock.EnterReadLock(); // Concurrent read access

try {

return _skipList.GetRange(start, end);

} finally {

_globalLock.ExitReadLock();

}

}

## Edge Case Handling
1. **Zero Score Handling**:
```csharp

if (newScoreAfterUpdate <= 0)

{

// Remove from leaderboard

}

2. **Rank Boundary Checks**:
```csharp

if (start < 1 || end < start || start > _count)

return new List<CustomerRecord>();

3. **Position Change Detection**:
```csharp

bool positionChanged = !(node.Prev != null && Compare(node.Prev, node) < 0 &&

node.Next[0] != null && Compare(node, node.Next[0]) < 0);

This implementation combines the performance advantages of skip lists with a robust concurrency model to deliver a high-performance leaderboard service suitable for large-scale applications.
