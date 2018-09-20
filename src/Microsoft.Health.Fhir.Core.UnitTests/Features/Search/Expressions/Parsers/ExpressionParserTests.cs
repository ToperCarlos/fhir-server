﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using NSubstitute;
using Xunit;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions.Parsers
{
    public class ExpressionParserTests
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ISearchValueExpressionBuilder _searchValueExpressionBuilder = Substitute.For<ISearchValueExpressionBuilder>();

        private readonly ExpressionParser _expressionParser;

        public ExpressionParserTests()
        {
            _expressionParser = new ExpressionParser(
                _searchParameterDefinitionManager,
                _searchValueExpressionBuilder);
        }

        [Fact]
        public void GivenAChainedParameterPointingToASingleResourceType_WhenParsed_ThenCorrectExpressionShouldBeCreated()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType targetResourceType = ResourceType.Organization;

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}.{param2}";
            string value = "Seattle";

            // Setup the search parameters.
            SetupReferenceSearchParameter(
                sourceResourceType,
                param1,
                targetResourceType);

            SearchParameter searchParameter = SetupSearchParameter(targetResourceType, param2);

            Expression expectedExpression = SetupExpression(searchParameter, value);

            // Parse the expression.
            Expression expression = _expressionParser.Parse(sourceResourceType, key, value);

            ValidateMultiaryExpression(
                expression,
                MultiaryOperator.Or,
                chainedExpression => ValidateChainedExpression(
                    chainedExpression,
                    sourceResourceType,
                    param1,
                    targetResourceType,
                    actualSearchExpression => Assert.Equal(expectedExpression, actualSearchExpression)));
        }

        [Fact]
        public void GivenAChainedParameterPointingToMultipleResourceTypes_WhenParsed_ThenCorrectExpressionShouldBeCreated()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType[] targetResourceTypes = new[] { ResourceType.Organization, ResourceType.Practitioner };

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}.{param2}";
            string value = "Seattle";

            // Setup the search parameters.
            SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceTypes);

            var expectedTargets = targetResourceTypes.Select(targetResourceType =>
            {
                SearchParameter searchParameter = SetupSearchParameter(targetResourceType, param2);

                Expression expectedExpression = SetupExpression(searchParameter, value);

                return new { TargetResourceType = targetResourceType, Expression = expectedExpression };
            })
            .ToArray();

            // Parse the expression.
            Expression expression = _expressionParser.Parse(sourceResourceType, key, value);

            ValidateMultiaryExpression(
                expression,
                MultiaryOperator.Or,
                expectedTargets.Select(expected =>
                {
                    return (Action<Expression>)(chainedExpression =>
                        ValidateChainedExpression(
                            chainedExpression,
                            sourceResourceType,
                            param1,
                            expected.TargetResourceType,
                            actualSearchExpression => Assert.Equal(expected.Expression, actualSearchExpression)));
                })
                .ToArray());
        }

        [Fact]
        public void GivenAChainedParameterPointingToMultipleResourceTypesAndWithResourceTypeSpecified_WhenParsed_ThenOnlyExpressionForTheSpecifiedResourceTypeShouldBeCreated()
        {
            ResourceType sourceResourceType = ResourceType.Patient;

            // The reference will support both Organization and Practitioner,
            // but we will limit the search to Organization only in the key below.
            ResourceType[] targetResourceTypes = new[] { ResourceType.Organization, ResourceType.Practitioner };

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}:Organization.{param2}";
            string value = "Seattle";

            // Setup the search parameters.
            SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceTypes);

            Expression[] expectedExpressions = targetResourceTypes.Select(targetResourceType =>
            {
                SearchParameter searchParameter = SetupSearchParameter(targetResourceType, param2);

                return SetupExpression(searchParameter, value);
            })
            .ToArray();

            // Parse the expression.
            Expression expression = _expressionParser.Parse(sourceResourceType, key, value);

            ValidateMultiaryExpression(
                expression,
                MultiaryOperator.Or,
                chainedExpression => ValidateChainedExpression(
                    chainedExpression,
                    sourceResourceType,
                    param1,
                    ResourceType.Organization,
                    actualSearchExpression => Assert.Equal(expectedExpressions[0], actualSearchExpression)));
        }

        [Fact]
        public void GivenAChainedParameterPointingToMultipleResourceTypesAndSearchParamIsNotSupportedByAllTargetResourceTypes_WhenParsed_ThenOnlyExpressionsForResourceTypeThatSupportsSearchParamShouldBeCreated()
        {
            ResourceType sourceResourceType = ResourceType.Patient;

            // The reference will support both Organization and Practitioner,
            // but the search value will only be supported by Practitioner.
            ResourceType[] targetResourceTypes = new[] { ResourceType.Organization, ResourceType.Practitioner };

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}.{param2}";
            string value = "Lewis";

            // Setup the search parameters.
            SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceTypes);

            // Setup the Organization to not support this search param.
            _searchParameterDefinitionManager.GetSearchParameter(ResourceType.Organization, param2)
                .Returns(x => throw new SearchParameterNotSupportedException(x.ArgAt<ResourceType>(0), x.ArgAt<string>(1)));

            // Setup the Practitioner to support this search param.
            SearchParameter searchParameter = SetupSearchParameter(ResourceType.Practitioner, param2);

            Expression expectedExpression = SetupExpression(searchParameter, value);

            // Parse the expression.
            Expression expression = _expressionParser.Parse(sourceResourceType, key, value);

            ValidateMultiaryExpression(
                expression,
                MultiaryOperator.Or,
                chainedExpression => ValidateChainedExpression(
                    chainedExpression,
                    sourceResourceType,
                    param1,
                    ResourceType.Practitioner,
                    actualSearchExpression => Assert.Equal(expectedExpression, actualSearchExpression)));
        }

        [Fact]
        public void GivenANestedChainedParameter_WhenParsed_ThenCorrectExpressionShouldBeCreated()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType firstTargetResourceType = ResourceType.Organization;
            ResourceType secondTargetResourceType = ResourceType.Practitioner;

            string param1 = "ref1";
            string param2 = "ref2";
            string param3 = "param";

            string key = $"{param1}.{param2}.{param3}";
            string value = "Microsoft";

            // Setup the search parameters.
            SetupReferenceSearchParameter(sourceResourceType, param1, firstTargetResourceType);
            SetupReferenceSearchParameter(firstTargetResourceType, param2, secondTargetResourceType);

            SearchParameter searchParameter = SetupSearchParameter(secondTargetResourceType, param3);

            Expression expectedExpression = SetupExpression(searchParameter, value);

            // Parse the expression.
            Expression expression = _expressionParser.Parse(sourceResourceType, key, value);

            ValidateMultiaryExpression(
                 expression,
                 MultiaryOperator.Or,
                 chainedExpression => ValidateChainedExpression(
                     chainedExpression,
                     sourceResourceType,
                     param1,
                     firstTargetResourceType,
                     nestedExpression => ValidateMultiaryExpression(
                         nestedExpression,
                         MultiaryOperator.Or,
                         nestedChainedExpression => ValidateChainedExpression(
                             nestedChainedExpression,
                             firstTargetResourceType,
                             param2,
                             secondTargetResourceType,
                             actualSearchExpression => Assert.Equal(expectedExpression, actualSearchExpression)))));
        }

        [Fact]
        public void GivenAModifier_WhenParsed_ThenExceptionShouldBeThrown()
        {
            ResourceType resourceType = ResourceType.Patient;

            string param1 = "ref";
            string modifier = "missing";

            // Practitioner is a valid resource type but is not supported by the search paramter.
            string key = $"{param1}:{modifier}";
            string value = "Seattle";

            SearchParameter searchParameter = SetupSearchParameter(resourceType, param1);

            Expression expression = Substitute.For<Expression>();

            _searchValueExpressionBuilder.Build(searchParameter, SearchParameter.SearchModifierCode.Missing, value).Returns(expression);

            // Parse the expression.
            Expression actualExpression = _expressionParser.Parse(resourceType, key, value);

            // The mock requires the modifier to match so if we get the same expression instance
            // then it means we got the modifier correctly.
            Assert.Equal(expression, actualExpression);
        }

        [Fact]
        public void GivenAChainedParameterThatIsNotReferenceType_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType sourceResourceType = ResourceType.Patient;

            string param1 = "ref1";

            string key = $"{param1}.param";
            string value = "Microsoft";

            // Setup the search parameters.
            SetupSearchParameter(sourceResourceType, param1);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(sourceResourceType, key, value));
        }

        [Fact]
        public void GivenAnInvalidResourceTypeToScope_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType targetResourceType = ResourceType.Organization;

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}:NonExistingResourceType.{param2}";

            SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceType);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(sourceResourceType, key, "Error"));
        }

        [Fact]
        public void GivenATargetResourceTypeThatIsNotSupported_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType targetResourceType = ResourceType.Organization;

            string param1 = "ref";
            string param2 = "param";

            // Practitioner is a valid resource type but is not supported by the search paramter.
            string key = $"{param1}:Practitioner.{param2}";

            SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceType);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(sourceResourceType, key, "Error"));
        }

        [Fact]
        public void GivenMultipleModifierSeparators_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType resourceType = ResourceType.Patient;

            SetupSearchParameter(resourceType, "param1");

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(resourceType, "param1:param2:param3", "Error"));
        }

        private SearchParameter SetupSearchParameter(ResourceType resourceType, string paramName)
        {
            SearchParameter searchParameter = new SearchParameter()
            {
                Name = paramName,
                Type = SearchParamType.String,
            };

            _searchParameterDefinitionManager.GetSearchParameter(resourceType, paramName).Returns(searchParameter);

            return searchParameter;
        }

        private void SetupReferenceSearchParameter(ResourceType resourceType, string paramName, params ResourceType[] targetResourceTypes)
        {
            _searchParameterDefinitionManager.GetSearchParameter(resourceType, paramName).Returns(
                new SearchParameter()
                {
                    Name = paramName,
                    Type = SearchParamType.Reference,
                    Target = targetResourceTypes.Cast<ResourceType?>(),
                });
        }

        private Expression SetupExpression(SearchParameter searchParameter, string value)
        {
            Expression expectedExpression = Substitute.For<Expression>();

            _searchValueExpressionBuilder.Build(searchParameter, null, value).Returns(expectedExpression);

            return expectedExpression;
        }
    }
}