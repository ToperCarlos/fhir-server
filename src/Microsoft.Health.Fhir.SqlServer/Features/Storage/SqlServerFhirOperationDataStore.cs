﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Conformance.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal class SqlServerFhirOperationDataStore : IFhirOperationDataStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly ILogger<SqlServerFhirOperationDataStore> _logger;
        private readonly JsonSerializerSettings _jsonSerializerSettings;

        public SqlServerFhirOperationDataStore(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            ILogger<SqlServerFhirOperationDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _logger = logger;

            _jsonSerializerSettings = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = new List<JsonConverter>
                {
                    new EnumLiteralJsonConverter(),
                },
            };
        }

        public async Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                V1.CreateExportJob.PopulateCommand(
                    sqlCommand,
                    jobRecord.Id,
                    jobRecord.Status.ToString(),
                    jobRecord.QueuedTime,
                    JsonConvert.SerializeObject(jobRecord, _jsonSerializerSettings));

                var rowVersion = (int?)await sqlCommand.ExecuteScalarAsync(cancellationToken);

                if (rowVersion == null)
                {
                    throw new OperationFailedException(string.Format(Core.Resources.OperationFailed, OperationsConstants.Export, Resources.NullRowVersion), HttpStatusCode.InternalServerError);
                }

                return new ExportJobOutcome(jobRecord, WeakETag.FromVersionId(rowVersion.ToString()));
            }
        }

        public Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<ExportJobOutcome> GetExportJobByHashAsync(string hash, CancellationToken cancellationToken)
        {
            // TODO: Implement this method (returning null for now to allow the create method to run).
            return Task.FromResult<ExportJobOutcome>(null);
        }

        public Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken)
        {
            // TODO: Implement this method.
            return Task.FromResult(new ExportJobOutcome(jobRecord, eTag));
        }

        public async Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                var jobHeartbeatTimeoutThresholdInSeconds = Convert.ToInt64(jobHeartbeatTimeoutThreshold.TotalSeconds);

                V1.AcquireExportJobs.PopulateCommand(
                    sqlCommand,
                    jobHeartbeatTimeoutThresholdInSeconds,
                    maximumNumberOfConcurrentJobsAllowed);

                var acquiredJobs = new List<ExportJobOutcome>();

                using (SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        (string rawJobRecord, byte[] rowVersionAsBytes) = sqlDataReader.ReadRow(V1.ExportJob.RawJobRecord, V1.ExportJob.JobVersion);

                        var exportJobRecord = JsonConvert.DeserializeObject<ExportJobRecord>(rawJobRecord, _jsonSerializerSettings);

                        if (BitConverter.IsLittleEndian)
                        {
                            Array.Reverse(rowVersionAsBytes);
                        }

                        var rowVersionAsDecimalString = BitConverter.ToInt64(rowVersionAsBytes, startIndex: 0).ToString();

                        acquiredJobs.Add(new ExportJobOutcome(exportJobRecord, WeakETag.FromVersionId(rowVersionAsDecimalString)));
                    }
                }

                return acquiredJobs;
            }
        }
    }
}
