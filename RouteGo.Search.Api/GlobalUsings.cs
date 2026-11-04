// Slice files carry no using directives — this file holds the shared set.
// The sibling slice skill and database setup EXTEND this list beneath the '// FILLED BY' marker below.
global using System.Text.Json;          // JsonSerializer, WireJson.Options
global using Microsoft.AspNetCore.Mvc;
global using RouteGo.Search.Api.Common;             // HttpClients, WireJson
global using RouteGo.Search.Api.Common.Ports;       // IClock
global using RouteGo.Search.Api.Common.Problems;    // ProblemResult, ProblemTypes (catalog projection)
global using RouteGo.Search.Api.Common.Behaviors;   // UnhandledExceptionHandler, ResponseMetadataMiddleware, ResponseHeadersTransformer
// FILLED BY the sibling slice skill — and by skills-db-postgres (copy-assets.ps1) at database setup:
global using System.Data.Common;   // skills-db-postgres (copy-assets.ps1)
global using Dapper;   // skills-db-postgres (copy-assets.ps1)
global using FluentValidation;
global using FluentValidation.Results;   // ValidationResult in the controller
global using Microsoft.AspNetCore.Authorization;   // every endpoint declares [Authorize] or [AllowAnonymous]
global using RouteGo.Search.Api.Domain;   // ReceivedEvent, EventHandlingOutcome for the trip-listing workers
