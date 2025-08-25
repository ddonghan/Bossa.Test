using Bossa.Test.HttpApi.Models;
using System.Collections.Generic;

namespace Bossa.Test.HttpApi.Services;

public interface IScoreboardService
{
    decimal UpdateScore(long customerId, decimal scoreChange);
    List<CustomerRecord> GetByRank(int start, int end);
    List<CustomerRecord> GetCustomerNeighbors(long customerId, int high, int low);
}