﻿// -----------------------------------------------------------------------
// <copyright file="CustomSqlTests.cs" company="Andrii Kurdiumov">
// Copyright (c) Andrii Kurdiumov. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace SqlMarshal.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class CustomSqlTests
    {
        [TestMethod]
        public void ScalarResult()
        {
            string source = @"
namespace Foo
{
    class C
    {
        private DbConnection connection;

        [SqlMarshal]
        public partial int M([RawSql]string sql, int clientId, string? personId);
    }
}";
            string output = this.GetGeneratedOutput(source, NullableContextOptions.Disable);

            Assert.IsNotNull(output);

            var expectedOutput = @"// <auto-generated>
// Code generated by Stored Procedures Code Generator.
// Changes may cause incorrect behavior and will be lost if the code is
// regenerated.
// </auto-generated>
#nullable enable
#pragma warning disable 1591

namespace Foo
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;

    partial class C
    {
        public partial int M(string sql, int clientId, string? personId)
        {
            var connection = this.connection;
            using var command = connection.CreateCommand();

            var clientIdParameter = command.CreateParameter();
            clientIdParameter.ParameterName = ""@client_id"";
            clientIdParameter.Value = clientId;

            var personIdParameter = command.CreateParameter();
            personIdParameter.ParameterName = ""@person_id"";
            personIdParameter.Value = personId == null ? (object)DBNull.Value : personId;

            var parameters = new DbParameter[]
            {
                clientIdParameter,
                personIdParameter,
            };

            command.CommandText = sql;
            command.Parameters.AddRange(parameters);
            var result = command.ExecuteScalar();
            return (int)result;
        }
    }
}";
            Assert.AreEqual(expectedOutput, output);
        }

        [TestMethod]
        public void MapResultSetToProcedure()
        {
            string source = @"
namespace Foo
{
    public class Item
    {
        public string StringValue { get; set; }
        public int Int32Value { get; set; }
        public int? NullableInt32Value { get; set; }
    }

    class C
    {
        private DbConnection connection;

        [SqlMarshal]
        public partial IList<Item> M([RawSql]string sql)
    }
}";
            string output = this.GetGeneratedOutput(source, NullableContextOptions.Disable);

            Assert.IsNotNull(output);

            var expectedOutput = @"// <auto-generated>
// Code generated by Stored Procedures Code Generator.
// Changes may cause incorrect behavior and will be lost if the code is
// regenerated.
// </auto-generated>
#nullable enable
#pragma warning disable 1591

namespace Foo
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Linq;

    partial class C
    {
        public partial IList<Foo.Item> M(string sql)
        {
            var connection = this.connection;
            using var command = connection.CreateCommand();

            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            var result = new List<Item>();
            while (reader.Read())
            {
                var item = new Item();
                var value_0 = reader.GetValue(0);
                item.StringValue = value_0 == DBNull.Value ? (string)null : (string)value_0;
                var value_1 = reader.GetValue(1);
                item.Int32Value = (int)value_1;
                var value_2 = reader.GetValue(2);
                item.NullableInt32Value = value_2 == DBNull.Value ? (int?)null : (int)value_2;
                result.Add(item);
            }

            reader.Close();
            return result;
        }
    }
}";
            Assert.AreEqual(expectedOutput, output);
        }

        private string GetGeneratedOutput(string source, NullableContextOptions nullableContextOptions)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source);

            var references = new List<MetadataReference>();
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                if (!assembly.IsDynamic && !string.IsNullOrWhiteSpace(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            var compilation = CSharpCompilation.Create(
                "foo",
                new SyntaxTree[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: nullableContextOptions));

            // var compileDiagnostics = compilation.GetDiagnostics();
            // Assert.IsFalse(compileDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Failed: " + compileDiagnostics.FirstOrDefault()?.GetMessage());
            ISourceGenerator generator = new Generator();

            var driver = CSharpGeneratorDriver.Create(generator);
            driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var generateDiagnostics);
            Assert.IsFalse(generateDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), "Failed: " + generateDiagnostics.FirstOrDefault()?.GetMessage());

            string output = outputCompilation.SyntaxTrees.Last().ToString();

            Console.WriteLine(output);

            return output;
        }
    }
}
