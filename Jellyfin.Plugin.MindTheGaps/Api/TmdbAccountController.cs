using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MindTheGaps.Model;
using Jellyfin.Plugin.MindTheGaps.Services.Tmdb;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MindTheGaps.Api;

/// <summary>
/// The TheMovieDb account connect flow, in two steps because TMDB needs the user to approve a request token
/// in their own browser. Nothing here needs a callback: the approval URL is opened without a redirect
/// parameter, so the server never has to be reachable from outside. Shares the <c>MindTheGaps</c> route.
/// </summary>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("MindTheGaps")]
[Produces("application/json")]
public class TmdbAccountController : ControllerBase
{
    private readonly TmdbAccountClient _account;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbAccountController"/> class.
    /// </summary>
    /// <param name="account">The TMDB account client.</param>
    public TmdbAccountController(TmdbAccountClient account)
    {
        _account = account;
    }

    /// <summary>
    /// Reports whether an account is connected, and as whom.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The connection status.</returns>
    [HttpGet("Tmdb/AccountStatus")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<TmdbAccountStatus>> GetAccountStatus(CancellationToken cancellationToken)
    {
        var config = Plugin.RequireConfiguration();
        if (!TmdbAccountClient.HasOwnApiKey())
        {
            return Ok(new TmdbAccountStatus
            {
                CanConnect = false,
                Message = "Enter your own TMDB API key first. A TMDB session belongs to the application that created it, and the built-in fallback key is Jellyfin's, shared by every install."
            });
        }

        if (string.IsNullOrWhiteSpace(config.TmdbSessionId))
        {
            return Ok(new TmdbAccountStatus { CanConnect = true, Message = "Not connected." });
        }

        var username = await _account.GetAccountNameAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new TmdbAccountStatus
        {
            CanConnect = true,
            Connected = username is not null,
            Username = username,
            Message = username is not null
                ? null
                : "The stored session no longer works. It may have been revoked on themoviedb.org, or the API key may have changed."
        });
    }

    /// <summary>
    /// Starts the connect flow: mints a request token and returns the page the user approves it on.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request token and its approval URL.</returns>
    [HttpPost("Tmdb/AccountConnect")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TmdbConnectStart>> StartConnect(CancellationToken cancellationToken)
    {
        if (!TmdbAccountClient.HasOwnApiKey())
        {
            return BadRequest("Enter your own TMDB API key before connecting an account.");
        }

        var token = await _account.CreateRequestTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            return BadRequest("TMDB refused to issue a request token. Check the API key.");
        }

        return Ok(new TmdbConnectStart
        {
            RequestToken = token,
            ApprovalUrl = TmdbAccountClient.ApprovalUrl(token)
        });
    }

    /// <summary>
    /// Finishes the connect flow, exchanging an approved request token for a session id and saving it.
    /// </summary>
    /// <param name="request">The request token the user approved.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting connection status.</returns>
    [HttpPost("Tmdb/AccountFinish")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TmdbAccountStatus>> FinishConnect(
        [FromBody] TmdbConnectFinish request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request?.RequestToken))
        {
            return BadRequest("No request token.");
        }

        var sessionId = await _account.CreateSessionAsync(request.RequestToken, cancellationToken).ConfigureAwait(false);
        if (sessionId is null)
        {
            return BadRequest("TMDB has not seen the approval yet. Open the approval page, click Approve, then try again.");
        }

        Save(sessionId);

        var username = await _account.GetAccountNameAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new TmdbAccountStatus { CanConnect = true, Connected = true, Username = username });
    }

    /// <summary>
    /// Forgets the stored session. The session still exists on TMDB until it is deleted there, so the message
    /// says where to revoke it properly.
    /// </summary>
    /// <returns>The resulting connection status.</returns>
    [HttpPost("Tmdb/AccountDisconnect")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<TmdbAccountStatus> Disconnect()
    {
        Save(string.Empty);
        return Ok(new TmdbAccountStatus
        {
            CanConnect = TmdbAccountClient.HasOwnApiKey(),
            Message = "Forgotten here. To revoke it at TMDB as well, remove this application under your themoviedb.org account settings."
        });
    }

    private static void Save(string sessionId)
    {
        var plugin = Plugin.Instance;
        if (plugin is null)
        {
            return;
        }

        // Saved on its own rather than through the settings form: the form would overwrite the session with
        // whatever the page last loaded, and the page never shows the session id.
        plugin.Configuration.TmdbSessionId = sessionId;
        plugin.SaveConfiguration();
    }
}
