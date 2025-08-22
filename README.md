# Leaderboard Service Implementation

## Project Overview
This service is a high-performance in-memory leaderboard system for managing customer scores and rankings. Built on .NET Core with a database-less design, all data is stored in memory. The system is optimized for high-concurrency scenarios, supporting large volumes of simultaneous customer operations.

## Design Highlights

### 1. High-Performance Memory Architecture
* **Partitioned Locking**: Uses `Environment.ProcessorCount * 4` partition locks with customer ID hashing
* **Read/Write Separation**: Fine-grained locking for writes and lock-free design for reads
* **Timed Snapshot Updates**: Background updates every 500ms using `ReaderWriterLockSlim` for consistency

### 2. Optimized Data Structures
* **Score Storage**: `ConcurrentDictionary<long, decimal>` for real-time customer scores
* **Leaderboard Snapshots**: `ImmutableSortedSet` provides thread-safe leaderboard views
* **Custom Comparator**: `LeaderboardComparer` sorts by score (descending) then customer ID (ascending)

### 3. API Implementation
Three core API endpoints implemented:

#### 1. Update Score
* **Endpoint**: `POST /customer/{customerid}/score/{score}`
* **Function**: Atomic customer score updates
* **Validation**:
  * `score` range constrained to [-1000, 1000]
  * Customers removed when score ≤ 0
* **Response**: `{ customerId, newScore }`

#### 2. Query by Rank Range
* **Endpoint**: `GET /leaderboard?start={start}&end={end}`
* **Function**: Retrieve customers within specified rank range
* **Defaults**: start=1, end=10
* **Performance**: Direct access to pre-sorted snapshot data

#### 3. Query Customer Neighbors
* **Endpoint**: `GET /leaderboard/{customerid}?high={high}&low={low}`
* **Function**: Get adjacent customers around specified customer
* **Defaults**: high=0, low=0
* **Boundary Handling**: Automatic range adjustment at leaderboard edges

## Performance Characteristics

| Operation | Time Complexity | Concurrency Control |
|----------|-----------------|---------------------|
| Update Score | O(1) average | Partitioned Locks |
| Rank Range Query | O(n) | Read Lock (RW Lock) |
| Neighbor Query | O(n) | Read Lock (RW Lock) |
| Snapshot Update | O(n log n) | Write Lock (RW Lock) |

## Running Instructions

### Environment Requirements
* .NET 6.0+ runtime
* Recommended: Multi-core CPU environment (better partition lock utilization)

### Startup Command
bash

dotnet run --project Bossa.Test.HttpApi

### Testing Examples

1. Update score:
bash

curl -X POST http://localhost:5000/customer/12345/score/100

2. Query leaderboard:
bash

curl http://localhost:5000/leaderboard?start=1&end=5

3. Query neighbors:
bash

curl http://localhost:5000/leaderboard/12345?high=2&low=2

## Design Decisions

1. **Timed Snapshot Updates**:
   * Balances real-time requirements with performance
   * 500ms interval provides optimal tradeoff
   * `_updateRequired` flag prevents unnecessary updates

2. **Partitioned Lock Selection**:
   * Lock count = CPU cores × 4
   * Customer ID-based hash distribution
   * Significantly reduces write contention

3. **Negative Score Handling**:
   * Automatic removal when score ≤ 0
   * Eliminates storage of invalid data
   * Reduces leaderboard maintenance overhead

4. **Immutable Data Structures**:
   * `ImmutableSortedSet` ensures thread-safe reads
   * Atomic snapshot updates
   * Zero-copy read advantages

This implementation meets high-concurrency and low-latency requirements while maintaining code simplicity and maintainability.
