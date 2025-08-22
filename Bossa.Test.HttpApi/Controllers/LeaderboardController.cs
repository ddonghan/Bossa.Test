

using Bossa.Test.HttpApi.Services;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
namespace Bossa.Test.HttpApi.Controllers;
[ApiController]
public class LeaderboardController : ControllerBase
{
    private readonly LeaderboardService _service;

    public LeaderboardController(LeaderboardService service)
    {
        _service = service;
    }

    /// <summary>
    /// Update Score ( POST /customer/{customerid}/score/{score})
    /// </summary>
    /// <param name="customerId">customerId</param>
    /// <param name="score">score</param>
    /// <returns></returns>
    [HttpPost("/customer/{customerid}/score/{score}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateScore(
        [FromRoute] long customerId,
        [FromRoute, Range(-1000, 1000)] decimal score)
    {
        var newScore = await _service.UpdateScoreAsync(customerId, score);
        if (newScore < 0) newScore = 0;
        return Ok(new { customerId,newScore});
    }

    /// <summary>
    ///  Query the rankings（ GET /leaderboard?start={start}&end={end}）
    /// </summary>
    /// <param name="start">start</param>
    /// <param name="end">end</param>
    /// <returns></returns>
    [HttpGet("/leaderboard")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLeaderboard(
        [FromQuery] int start = 1,
        [FromQuery] int end = 10)
    {
        var result = await _service.GetByRankRangeAsync(start, end);
        return Ok(result);
    }

    /// <summary>
    ///  Check the rankings near customers（ GET /leaderboard/{customerid}?high={high}&low={low}）
    /// </summary>
    /// <param name="customerId">customerId</param>
    /// <param name="high">high</param>
    /// <param name="low">low</param>
    /// <returns></returns>
    [HttpGet("/leaderboard/{customerid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetNeighborRank(
        [FromRoute] long customerId,
        [FromQuery] int high = 0,
        [FromQuery] int low = 0)
    {
        var result = await _service.GetNeighborRangeAsync(customerId, high, low);
        return Ok(result);
    }
}
 