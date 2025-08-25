using Bossa.Test.HttpApi.Models;
using Bossa.Test.HttpApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace Bossa.Test.HttpApi.Controllers;
[ApiController]
[Route("leaderboard")]
public class LeaderboardController : ControllerBase
{
    private readonly IScoreboardService _scoreboardService;

    public LeaderboardController(IScoreboardService scoreboardService)
    {
        _scoreboardService = scoreboardService;
    }

    [HttpGet]
    public IActionResult GetByRank([FromQuery] int start, [FromQuery] int end)
    {
        try
        {
            if (start < 1 || end < start)
            {
                return BadRequest("Invalid rank range");
            }

            var results = _scoreboardService.GetByRank(start, end);
            return Ok(results);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }

    [HttpGet("{customerId}")]
    public IActionResult GetCustomerNeighbors(
        long customerId,
        [FromQuery] int high = 0,
        [FromQuery] int low = 0)
    {
        try
        {
            if (high < 0 || low < 0)
            {
                return BadRequest("High and low must be non-negative");
            }

            var results = _scoreboardService.GetCustomerNeighbors(customerId, high, low);
            return Ok(results);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}