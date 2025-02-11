﻿using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Signals.Aspects.Auth.Extensions;
using Signals.Aspects.Auth.NetCore.Extensions;
using System;
using System.Security.Claims;
using System.Threading;

namespace Signals.Aspects.Auth.NetCore
{
    /// <summary>
    /// Authentication manager
    /// </summary>
    public class AuthenticationManager : IAuthenticationManager
    {
        /// <summary>
        /// Http request context
        /// </summary>
        protected HttpContext Context { get; set; }

        /// <summary>
        /// CTOR
        /// </summary>
        /// <param name="contextAccessor"></param>
        public AuthenticationManager(IHttpContextAccessor contextAccessor)
        {
            Context = contextAccessor.HttpContext;
        }

        /// <summary>
        /// CTOR
        /// </summary>
        public AuthenticationManager()
        {

        }

        /// <summary>
        /// Get currently logged in user principal
        /// </summary>
        /// <returns></returns>
        public ClaimsPrincipal GetCurrentPrincipal()
        {
            if (Context != null)
            {
                if (Context?.User?.Identity?.IsAuthenticated == true)
                    return Context.User;
                else
                    return null;
            }

            if (Thread.CurrentPrincipal?.Identity?.IsAuthenticated == true)
                return Thread.CurrentPrincipal as ClaimsPrincipal;

            return null;
        }

        /// <summary>
        /// Get currently logged in user
        /// </summary>
        /// <returns></returns>
        public T GetCurrentUser<T>() where T : class
        {
            var principal = GetCurrentPrincipal();

            return principal?.GetClaim<T>(ClaimTypes.UserData);
        }

        /// <summary>
        /// Login user with addiitonal data
        /// </summary>
        /// <param name="principal"></param>
        /// <param name="properties"></param>
        public void Login(ClaimsPrincipal principal, AuthenticationProperties properties = null)
        {
            Login<object>(principal, null, properties);
        }

        /// <summary>
        /// Login user with addiitonal data
        /// </summary>
        /// <param name="principal"></param>
        /// <param name="user"></param>
        /// <param name="properties"></param>
        public void Login<T>(ClaimsPrincipal principal, T user, AuthenticationProperties properties = null) where T : class
        {
            if (principal == null) throw new ArgumentNullException(nameof(principal));

            if (user != null)
            {
                (principal.Identity as ClaimsIdentity)?.AddClaims(user.ExtractClaims());
                principal.AddClaim(ClaimTypes.UserData, user);
            }

            if (properties != null) principal.AddClaim(Auth.Extensions.PrincipalExtensions.AuthenticationPropertiesClaimName, properties);

            var claimProperties = principal.GetClaim<AuthenticationProperties>(Auth.Extensions.PrincipalExtensions.AuthenticationPropertiesClaimName);

            if (Context != null)
            {
                var scheme = principal.Identity.AuthenticationType ?? CookieAuthenticationDefaults.AuthenticationScheme;

                if (scheme == CookieAuthenticationDefaults.AuthenticationScheme)
                {
                    Context.SignInAsync(scheme, principal, claimProperties.To()).Wait();
                }
                Context.User = principal;
            }

            Thread.CurrentPrincipal = principal;
        }

        /// <summary>
        /// Logout currently logged in user
        /// </summary>
        public void Logout()
        {
            Context?.SignOutAsync().Wait();
            Thread.CurrentPrincipal = null;
            if (Context != null) Context.User = null;
        }

        /// <summary>
        /// Set currently logged user data
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="user"></param>
        public void SetCurrentUser<T>(T user) where T : class
        {
            var principal = GetCurrentPrincipal();
            if (principal == null) return;

            // Save the auth properties
            var claimProperties = principal.GetClaim<AuthenticationProperties>(Auth.Extensions.PrincipalExtensions.AuthenticationPropertiesClaimName);

            // Perfomr logout
            Logout();

            // Log the user back in
            Login(principal, user, claimProperties);
        }
    }
}
