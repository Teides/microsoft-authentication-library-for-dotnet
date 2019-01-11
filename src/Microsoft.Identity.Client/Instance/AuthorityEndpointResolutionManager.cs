﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Identity.Client.AppConfig;
using Microsoft.Identity.Client.Core;
using Microsoft.Identity.Client.Exceptions;
using Microsoft.Identity.Client.OAuth2;

namespace Microsoft.Identity.Client.Instance
{
    internal class AuthorityEndpointResolutionManager : IAuthorityEndpointResolutionManager
    {
        private static readonly ConcurrentDictionary<string, AuthorityEndpointCacheEntry> EndpointCacheEntries =
            new ConcurrentDictionary<string, AuthorityEndpointCacheEntry>();

        private readonly IServiceBundle _serviceBundle;

        public AuthorityEndpointResolutionManager(IServiceBundle serviceBundle, bool shouldClearCache = true)
        {
            _serviceBundle = serviceBundle;
            if (shouldClearCache)
            {
                EndpointCacheEntries.Clear();
            }
        }

        public async Task<AuthorityEndpoints> ResolveEndpointsAsync(
            AuthorityInfo authorityInfo,
            string userPrincipalName,
            RequestContext requestContext)
        {
            if (authorityInfo.AuthorityType == AuthorityType.Adfs && string.IsNullOrEmpty(userPrincipalName))
            {
                throw MsalExceptionFactory.GetClientException(
                    CoreErrorCodes.UpnRequired,
                    CoreErrorMessages.UpnRequiredForAuthroityValidation);
            }

            if (TryGetCacheValue(authorityInfo, userPrincipalName, out AuthorityEndpoints endpoints))
            {
                requestContext.Logger.Info("Resolving authority endpoints... Already resolved? - TRUE");
                return endpoints;
            }

            requestContext.Logger.Info("Resolving authority endpoints... Already resolved? - FALSE");

            var authorityUri = new Uri(authorityInfo.CanonicalAuthority);
            string path = authorityUri.AbsolutePath.Substring(1);
            string tenant = path.Substring(0, path.IndexOf("/", StringComparison.Ordinal));
            bool isTenantless = Authority.TenantlessTenantNames.Contains(tenant.ToLowerInvariant());

            // TODO: where is the value in this log message?  we have a bunch of code supporting printing just this out...
            requestContext.Logger.Info("Is Authority tenantless? - " + isTenantless);

            IOpenIdConfigurationEndpointManager endpointManager =
                OpenIdConfigurationEndpointManagerFactory.Create(authorityInfo, _serviceBundle);

            string openIdConfigurationEndpoint = await endpointManager.GetOpenIdConfigurationEndpointAsync(
                                                     authorityInfo,
                                                     userPrincipalName,
                                                     requestContext).ConfigureAwait(false);

            // Discover endpoints via openid-configuration
            TenantDiscoveryResponse edr =
                await DiscoverEndpointsAsync(openIdConfigurationEndpoint, requestContext).ConfigureAwait(false);

            if (string.IsNullOrEmpty(edr.AuthorizationEndpoint))
            {
                throw MsalExceptionFactory.GetClientException(
                    CoreErrorCodes.TenantDiscoveryFailedError,
                    "Authorize endpoint was not found in the openid configuration");
            }

            if (string.IsNullOrEmpty(edr.TokenEndpoint))
            {
                throw MsalExceptionFactory.GetClientException(
                    CoreErrorCodes.TenantDiscoveryFailedError,
                    "Token endpoint was not found in the openid configuration");
            }

            if (string.IsNullOrEmpty(edr.Issuer))
            {
                throw MsalExceptionFactory.GetClientException(
                    CoreErrorCodes.TenantDiscoveryFailedError,
                    "Issuer was not found in the openid configuration");
            }

            endpoints = new AuthorityEndpoints(
                edr.AuthorizationEndpoint.Replace("{tenant}", tenant),
                edr.TokenEndpoint.Replace("{tenant}", tenant),
                edr.Issuer.Replace("{tenant}", tenant));

            Add(authorityInfo, userPrincipalName, endpoints);
            return endpoints;
        }

        private bool TryGetCacheValue(AuthorityInfo authorityInfo, string userPrincipalName, out AuthorityEndpoints endpoints)
        {
            endpoints = null;

            if (!EndpointCacheEntries.TryGetValue(authorityInfo.CanonicalAuthority, out AuthorityEndpointCacheEntry cacheEntry))
            {
                return false;
            }

            if (authorityInfo.AuthorityType != AuthorityType.Adfs)
            {
                endpoints = cacheEntry.Endpoints;
                return true;
            }

            if (!cacheEntry.ValidForDomainsList.Contains(AdfsUpnHelper.GetDomainFromUpn(userPrincipalName)))
            {
                return false;
            }

            endpoints = cacheEntry.Endpoints;
            return true;
        }

        private void Add(AuthorityInfo authorityInfo, string userPrincipalName, AuthorityEndpoints endpoints)
        {
            var updatedCacheEntry = new AuthorityEndpointCacheEntry(endpoints);

            if (authorityInfo.AuthorityType == AuthorityType.Adfs)
            {
                // Since we're here, we've made a call to the backend.  We want to ensure we're caching
                // the latest values from the server.
                if (EndpointCacheEntries.TryGetValue(
                    authorityInfo.CanonicalAuthority,
                    out AuthorityEndpointCacheEntry cacheEntry))
                {
                    foreach (string s in cacheEntry.ValidForDomainsList)
                    {
                        updatedCacheEntry.ValidForDomainsList.Add(s);
                    }
                }

                updatedCacheEntry.ValidForDomainsList.Add(AdfsUpnHelper.GetDomainFromUpn(userPrincipalName));
            }

            EndpointCacheEntries.TryAdd(authorityInfo.CanonicalAuthority, updatedCacheEntry);
        }

        private async Task<TenantDiscoveryResponse> DiscoverEndpointsAsync(
            string openIdConfigurationEndpoint,
            RequestContext requestContext)
        {
            var client = new OAuth2Client(_serviceBundle.HttpManager, _serviceBundle.TelemetryManager);
            return await client.ExecuteRequestAsync<TenantDiscoveryResponse>(
                       new Uri(openIdConfigurationEndpoint),
                       HttpMethod.Get,
                       requestContext).ConfigureAwait(false);
        }

        private class AuthorityEndpointCacheEntry
        {
            public AuthorityEndpointCacheEntry(AuthorityEndpoints endpoints)
            {
                Endpoints = endpoints;
            }

            public AuthorityEndpoints Endpoints { get; }
            public HashSet<string> ValidForDomainsList { get; } = new HashSet<string>();
        }
    }
}
