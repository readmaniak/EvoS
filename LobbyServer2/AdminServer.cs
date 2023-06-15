using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CentralServer.ApiServer;
using EvoS.DirectoryServer.Account;
using EvoS.Framework;
using EvoS.Framework.DataAccess;
using EvoS.Framework.Network.Static;
using log4net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using WebSocketSharp;

namespace CentralServer;

public class AdminServer
{
    private static readonly ILog log = LogManager.GetLogger(typeof(AdminServer));

    private static readonly string TokenIssuer = "AtlasReactor";
    private static readonly string TokenAudience = "AtlasReactor";
    
    public WebApplication Init()
    {
        string apiKey = EvosConfiguration.GetApiKey();
        if (CollectionUtilities.IsNullOrEmpty(apiKey))
        {
            log.Info("Api server is not enabled");
            return null;
        }
        
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddAuthentication()
            .AddJwtBearer(o =>
            {
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = TokenIssuer,
                    ValidAudience = TokenAudience,
                    IssuerSigningKey = Key(apiKey),
                    // ClockSkew = TimeSpan.Zero,
                };
            });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("api_readonly", policy => policy.RequireRole("api_readonly"));
        var app = builder.Build();
        
        app.MapPost("/api/login", Login).AllowAnonymous();
        app.MapGet("/api/status", CommonController.GetStatus).RequireAuthorization("api_readonly");
        _ = app.RunAsync("http://localhost:3000");
        
        log.Info("Started admin server localhost:3000");
        return app;
    }

    private static SymmetricSecurityKey Key(string key)
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
    }

    private static IResult Login(string UserName, string Password)
    {
        if (UserName.IsNullOrEmpty() || Password.IsNullOrEmpty())
        {
            log.Info($"Attempt to login for api access without credentials");
            return Results.Unauthorized();
        }
        long accountId;
        try
        {
            accountId = LoginManager.Login(new AuthInfo { UserName = UserName, Password = Password });
        }
        catch (Exception _)
        {
            log.Info($"Failed to authorize {UserName} for api access");
            return Results.Unauthorized();
        }
        PersistedAccountData account = DB.Get().AccountDao.GetAccount(accountId);
        if (!account.AccountComponent.AppliedEntitlements.ContainsKey("DEVELOPER_ACCESS"))
        {
            log.Info($"{UserName} attempted to get api access");
            return Results.Unauthorized();
        }
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Role, "api_readonly")
            }),
            Expires = DateTime.UtcNow.AddMinutes(60),
            Issuer = TokenIssuer,
            Audience = TokenAudience,
            SigningCredentials = new SigningCredentials(Key(EvosConfiguration.GetApiKey()), SecurityAlgorithms.HmacSha512Signature)
        };
        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var stringToken = tokenHandler.WriteToken(token);
        log.Info($"{UserName} logged in for api access");
        return Results.Ok(stringToken);
    }
}