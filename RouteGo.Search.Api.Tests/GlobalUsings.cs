// Slice test files (linked in from {Project}/Features/**/*.Tests.cs) carry no using directives —
// this file holds the test project's shared set. The sibling slice skill EXTENDS it as its
// test helpers need more (e.g. Testcontainers, AngleSharp).
global using Xunit;
global using RouteGo.Search.Api.Common;             // HttpClients, WireJson
global using System.Net;                            // HttpStatusCode
global using System.Net.Http.Json;                  // ReadFromJsonAsync
global using RouteGo.Search.Api.Common.Ports;       // IClock
// Not System.Text.Json or Microsoft.Extensions.DependencyInjection: the stamped test-host files import
// those themselves, and a global copy would make their own usings redundant (IDE0005).
global using RouteGo.Search.Api.Tests;              // ApiFactory
global using RouteGo.Search.Api.Tests.Database;     // DatabaseFixture, DatabaseCollection, TestHost
global using RouteGo.Search.Api.Tests.Support;      // trip-listing worker test helpers
global using RouteGo.Search.Api.Domain;             // ReceivedEvent, EventHandlingOutcome
global using System.Text.Json.Nodes;                // event data built field by field
