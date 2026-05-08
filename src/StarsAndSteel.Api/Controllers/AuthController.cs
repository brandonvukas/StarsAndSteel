using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using StarsAndSteel.Api.Auth;
using StarsAndSteel.Api.Auth.Dtos;
using StarsAndSteel.Core.Entities;

namespace StarsAndSteel.Api.Controllers;

/// <summary>
/// Account creation and session endpoints. The Identity rules and password
/// requirements are configured globally in Program.cs. Rate-limited via the
/// "auth" policy (5 requests/minute/IP) — see docs/10 §"Rate limiting".
/// </summary>
[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        ITokenService tokenService,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _logger = logger;
    }

    /// <summary>
    /// Creates a User. Does not auto-login — the SPA explicitly calls /login afterward
    /// so the same code path always runs (and the same audit logs trigger).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _registerValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(BuildModelState(validation.Errors));
        }

        // Identity uniqueness check — covers email and username collisions in one call.
        var existingByEmail = await _userManager.FindByEmailAsync(request.Email);
        if (existingByEmail is not null)
        {
            return Conflict(new { error = "An account with that email already exists." });
        }

        var existingByName = await _userManager.FindByNameAsync(request.DisplayName);
        if (existingByName is not null)
        {
            return Conflict(new { error = "That display name is taken." });
        }

        var user = new User
        {
            UserName = request.DisplayName,
            Email = request.Email,
            DisplayName = request.DisplayName,
            CreatedAt = DateTime.UtcNow,
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            // Identity returns its own validation errors (password complexity, etc.).
            // Surface them as a 400 with a model-state-shaped payload.
            var modelState = new ModelStateDictionary();
            foreach (var error in result.Errors)
            {
                modelState.AddModelError(error.Code, error.Description);
            }
            return ValidationProblem(modelState);
        }

        _logger.LogInformation(
            "User registered: {UserId} {DisplayName} from {RemoteIp}",
            user.Id, user.DisplayName, HttpContext.Connection.RemoteIpAddress);

        return CreatedAtAction(nameof(Me), routeValues: null, value: new
        {
            userId = user.Id,
            displayName = user.DisplayName,
            email = user.Email,
        });
    }

    /// <summary>
    /// Validates credentials, sets the auth cookie via SignInManager, and returns
    /// a JWT for SignalR. See docs/10 §"Why two creds (cookie + JWT)?".
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var validation = await _loginValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(BuildModelState(validation.Errors));
        }

        // Accept either email or display name; resolve to a user before checking the password.
        var user = request.EmailOrDisplayName.Contains('@')
            ? await _userManager.FindByEmailAsync(request.EmailOrDisplayName)
            : await _userManager.FindByNameAsync(request.EmailOrDisplayName);

        if (user is null)
        {
            _logger.LogInformation(
                "Login failed (unknown user) for {Identifier} from {RemoteIp}",
                request.EmailOrDisplayName, HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(new { error = "Invalid credentials." });
        }

        // PasswordSignInAsync sets the cookie and increments lockout counters.
        var signIn = await _signInManager.PasswordSignInAsync(
            user, request.Password, isPersistent: true, lockoutOnFailure: true);

        if (!signIn.Succeeded)
        {
            _logger.LogInformation(
                "Login failed for {UserId} ({DisplayName}) from {RemoteIp}: {Reason}",
                user.Id, user.DisplayName, HttpContext.Connection.RemoteIpAddress,
                signIn.IsLockedOut ? "locked-out" : signIn.IsNotAllowed ? "not-allowed" : "bad-password");
            return Unauthorized(new { error = "Invalid credentials." });
        }

        var (accessToken, expiresAt) = _tokenService.IssueAccessToken(user);

        _logger.LogInformation(
            "Login succeeded: {UserId} {DisplayName} from {RemoteIp}",
            user.Id, user.DisplayName, HttpContext.Connection.RemoteIpAddress);

        return Ok(new AuthResponse(
            UserId: user.Id,
            DisplayName: user.DisplayName,
            Email: user.Email ?? string.Empty,
            AccessToken: accessToken,
            AccessTokenExpiresAt: expiresAt));
    }

    /// <summary>
    /// Clears the auth cookie. The JWT is stateless and simply expires; we don't
    /// maintain a revocation list (lifetime is 15 min, see docs/10).
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    /// <summary>
    /// Returns the calling user's identity. 401 if not signed in.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<MeResponse>> Me()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(new MeResponse(
            UserId: user.Id,
            DisplayName: user.DisplayName,
            Email: user.Email ?? string.Empty));
    }

    private static ModelStateDictionary BuildModelState(IEnumerable<FluentValidation.Results.ValidationFailure> failures)
    {
        var modelState = new ModelStateDictionary();
        foreach (var failure in failures)
        {
            modelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
        }
        return modelState;
    }
}
