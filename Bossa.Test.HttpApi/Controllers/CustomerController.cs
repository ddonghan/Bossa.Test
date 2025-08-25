using Bossa.Test.HttpApi.Services;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("customer")]
public class CustomerController : ControllerBase
{
    private readonly IScoreboardService _scoreboardService;

    public CustomerController(IScoreboardService scoreboardService)
    {
        _scoreboardService = scoreboardService;
    }

    [HttpPost("{customerId}/score/{score}")]
    public IActionResult UpdateScore(long customerId, decimal score)
    {
        try
        {
            if (score < -1000 || score > 1000)
            {
                return BadRequest("Score must be between -1000 and +1000");
            }

            var newScore = _scoreboardService.UpdateScore(customerId, score);
            return Ok(newScore);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Internal server error: {ex.Message}");
        }
    }
}