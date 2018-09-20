﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Validation.Narratives;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of FHIR components
    /// </summary>
    public class FhirModule : IStartupModule
    {
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            var jsonParser = new FhirJsonParser();
            var jsonSerializer = new FhirJsonSerializer();

            var xmlParser = new FhirXmlParser();
            var xmlSerializer = new FhirXmlSerializer();

            services.AddSingleton(jsonParser);
            services.AddSingleton(jsonSerializer);
            services.AddSingleton(xmlParser);
            services.AddSingleton(xmlSerializer);

            services.Add<FormatterConfiguration>()
                .Singleton()
                .AsSelf()
                .AsService<IPostConfigureOptions<MvcOptions>>()
                .AsService<IProvideCapability>();

            services.AddSingleton<OperationOutcomeExceptionFilterAttribute>();
            services.AddSingleton<ValidateContentTypeFilterAttribute>();

            services.TypesInSameAssemblyAs<FhirJsonInputFormatter>()
                .AssignableTo<TextInputFormatter>()
                .Singleton()
                .AsSelf();

            services.TypesInSameAssemblyAs<FhirJsonOutputFormatter>()
                .AssignableTo<TextOutputFormatter>()
                .Singleton()
                .AsSelf();

            services.AddSingleton<IFhirContextAccessor, FhirContextAccessor>();
            services.AddSingleton<CorrelationIdProvider>(provider => () => Guid.NewGuid().ToString());

            // Add conformance provider for implementation metadata.
            services.AddSingleton<IConfiguredConformanceProvider, DefaultConformanceProvider>();

            services.Add<ConformanceProvider>()
                .Singleton()
                .AsSelf()
                .AsService<IConformanceProvider>();

            services.Add<SystemConformanceProvider>()
                .Singleton()
                .AsSelf()
                .AsService<ISystemConformanceProvider>();

            services.Add<SecurityProvider>()
                .Singleton()
                .AsSelf()
                .AsService<IProvideCapability>();

            services.TypesInSameAssemblyAs<IProvideCapability>()
                .AssignableTo<IProvideCapability>()
                .Transient()
                .AsService<IProvideCapability>();

            services.AddSingleton<INarrativeHtmlSanitizer, NarrativeHtmlSanitizer>();

            // Register a factory to resolve an owned scope that returns all components that provide capabilities
            services.AddFactory<IOwned<IEnumerable<IProvideCapability>>>();

            // Register Owned as an Open Generic, this can resolve any service with an owned lifetime scope
            services.AddTransient(typeof(IOwned<>), typeof(Owned<>));

            // Register Lazy<> as an Open Generic, this can resolve any service with Lazy instantiation
            services.AddTransient(typeof(Lazy<>), typeof(LazyProvider<>));
        }
    }
}