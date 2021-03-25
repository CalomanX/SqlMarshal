﻿// -----------------------------------------------------------------------
// <copyright file="Generator.cs" company="Andrii Kurdiumov">
// Copyright (c) Andrii Kurdiumov. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace StoredProcedureSourceGenerator
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Text;

    /// <summary>
    /// Stored procedures generator.
    /// </summary>
    [Generator]
    public class Generator : ISourceGenerator
    {
        private const string AttributeSource = @"// <auto-generated>
// Code generated by Stored Procedures Code Generator.
// Changes may cause incorrect behavior and will be lost if the code is
// regenerated.
// </auto-generated>
#nullable disable

[System.AttributeUsage(System.AttributeTargets.Method, AllowMultiple=true)]
internal sealed class StoredProcedureGeneratedAttribute: System.Attribute
{
    public StoredProcedureGeneratedAttribute(string name)
        => (StoredProcedureName) = (name);

    public string StoredProcedureName { get; }
}
";

        /// <inheritdoc/>
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization((pi) => pi.AddSource("StoredProcedureGeneratedAttribute.cs", AttributeSource));
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        /// <inheritdoc/>
        public void Execute(GeneratorExecutionContext context)
        {
            // Retrieve the populated receiver
            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
            {
                return;
            }

            INamedTypeSymbol? attributeSymbol = context.Compilation.GetTypeByMetadataName("StoredProcedureGeneratedAttribute");
            if (attributeSymbol == null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor("SP0001", "No stored procedure attribute", "Internal analyzer error.", "Internal", DiagnosticSeverity.Error, true),
                    null));
                return;
            }

            var hasNullableAnnotations = context.Compilation.Options.NullableContextOptions != NullableContextOptions.Disable;

            // Group the fields by class, and generate the source
            foreach (IGrouping<ISymbol?, IMethodSymbol> group in receiver.Methods.GroupBy(f => f.ContainingType, SymbolEqualityComparer.Default))
            {
                var key = (INamedTypeSymbol)group.Key!;
                var sourceCode = this.ProcessClass(
                    (INamedTypeSymbol)group.Key!,
                    group.ToList(),
                    attributeSymbol,
                    hasNullableAnnotations);
                if (sourceCode == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        new DiagnosticDescriptor("SP0002", "No source code generated attribute", "Internal analyzer error.", "Internal", DiagnosticSeverity.Error, true),
                        null));
                    continue;
                }

                context.AddSource($"{key.ToDisplayString().Replace(".", "_")}_sp.cs", SourceText.From(sourceCode, Encoding.UTF8));
            }
        }

        private static string GetAccessibility(Accessibility a)
        {
            return a switch
            {
                Accessibility.Public => "public",
                Accessibility.Friend => "internal",
                _ => string.Empty,
            };
        }

        private static INamedTypeSymbol GetUnderlyingType(INamedTypeSymbol namedTypeSymbol)
        {
            if (namedTypeSymbol.Name == "Nullable")
            {
                return (INamedTypeSymbol)namedTypeSymbol.TypeArguments[0];
            }

            return namedTypeSymbol;
        }

        private static ISymbol? GetDbSetField(IFieldSymbol? dbContextSymbol, ITypeSymbol itemTypeSymbol)
        {
            if (dbContextSymbol == null)
            {
                return null;
            }

            var members = dbContextSymbol.Type.GetMembers().OfType<IPropertySymbol>();
            foreach (var fieldSymbol in members)
            {
                var fieldType = fieldSymbol.Type;
                if (fieldType is INamedTypeSymbol namedTypeSymbol)
                {
                    namedTypeSymbol = GetUnderlyingType(namedTypeSymbol);
                    if (namedTypeSymbol.Name == "DbSet"
                        && namedTypeSymbol.TypeArguments.Length == 1
                        && namedTypeSymbol.TypeArguments[0].Name == itemTypeSymbol.Name)
                    {
                        return fieldSymbol;
                    }
                }
            }

            return null;
        }

        private static IFieldSymbol? GetContextField(INamedTypeSymbol classSymbol)
        {
            var fieldSymbols = classSymbol.GetMembers().OfType<IFieldSymbol>();
            foreach (var memberSymbol in fieldSymbols)
            {
                var baseType = memberSymbol.Type.BaseType;
                if (baseType == null)
                {
                    continue;
                }

                if (baseType.Name == "DbContext")
                {
                    return memberSymbol;
                }
            }

            return null;
        }

        private static IFieldSymbol? GetConnectionField(INamedTypeSymbol classSymbol)
        {
            var fieldSymbols = classSymbol.GetMembers().OfType<IFieldSymbol>();
            foreach (var memberSymbol in fieldSymbols)
            {
                if (memberSymbol.Type.Name == "DbConnection")
                {
                    return memberSymbol;
                }

                var baseType = memberSymbol.Type.BaseType;
                if (baseType == null)
                {
                    continue;
                }

                if (baseType.Name == "DbConnection")
                {
                    return memberSymbol;
                }
            }

            return null;
        }

        private static ITypeSymbol GetUnderlyingType(ITypeSymbol returnType)
        {
            if (returnType is INamedTypeSymbol namedTypeSymbol)
            {
                if (!namedTypeSymbol.IsGenericType || namedTypeSymbol.TypeArguments.Length != 1)
                {
                    return returnType;
                }

                return namedTypeSymbol.TypeArguments[0];
            }

            return returnType;
        }

        private static bool IsScalarType(ITypeSymbol returnType)
        {
            return returnType.SpecialType switch
            {
                SpecialType.System_String => true,
                SpecialType.System_Boolean => true,
                SpecialType.System_Byte => true,
                SpecialType.System_Int32 => true,
                SpecialType.System_Int64 => true,
                SpecialType.System_DateTime => true,
                SpecialType.System_Decimal => true,
                SpecialType.System_Double => true,
                _ => false,
            };
        }

        private static string GetParameterDeclaration(IParameterSymbol parameter)
        {
            if (parameter.RefKind == RefKind.Out)
            {
                return $"out {parameter.Type.ToDisplayString()} {parameter.Name}";
            }

            if (parameter.RefKind == RefKind.Ref)
            {
                return $"ref {parameter.Type.ToDisplayString()} {parameter.Name}";
            }

            return $"{parameter.Type.ToDisplayString()} {parameter.Name}";
        }

        private static string GetParameterPassing(IParameterSymbol parameter)
        {
            if (parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref)
            {
                return "@" + NameMapper.MapName(parameter.Name) + " OUTPUT";
            }

            return "@" + NameMapper.MapName(parameter.Name);
        }

        private static string GetParameterSqlDbType(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol namedTypeSymbol)
            {
                if (type.Name == "Nullable")
                {
                    return GetParameterSqlDbType(namedTypeSymbol.TypeArguments[0]);
                }
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_String:
                    return "System.Data.DbType.String";
                case SpecialType.System_Byte:
                    return "System.Data.DbType.Byte";
                case SpecialType.System_SByte:
                    return "System.Data.DbType.SByte";
                case SpecialType.System_Int16:
                    return "System.Data.DbType.Int16";
                case SpecialType.System_Int32:
                    return "System.Data.DbType.Int32";
                case SpecialType.System_Int64:
                    return "System.Data.DbType.Int64";
                case SpecialType.System_UInt16:
                    return "System.Data.DbType.UInt16";
                case SpecialType.System_UInt32:
                    return "System.Data.DbType.UInt32";
                case SpecialType.System_UInt64:
                    return "System.Data.DbType.UInt64";
                case SpecialType.System_Single:
                    return "System.Data.DbType.Single";
                case SpecialType.System_Double:
                    return "System.Data.DbType.Double";
                case SpecialType.System_DateTime:
                    return "System.Data.DbType.DateTime2";
                default:
                    throw new System.NotImplementedException();
            }
        }

        private static void DeclareParameter(StringBuilder source, bool hasNullableAnnotations, IParameterSymbol parameter)
        {
            var requireParameterNullCheck = parameter.Type.CanHaveNullValue(hasNullableAnnotations);
            source.Append($@"            var {parameter.Name}Parameter = command.CreateParameter();
            {parameter.Name}Parameter.ParameterName = ""@{NameMapper.MapName(parameter.Name)}"";
");
            if (parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref)
            {
                var parameterSqlDbType = GetParameterSqlDbType(parameter.Type);
                source.Append($@"            {parameter.Name}Parameter.DbType = {parameterSqlDbType};
");
                var direction = parameter.RefKind == RefKind.Out ? "System.Data.ParameterDirection.Output" : "System.Data.ParameterDirection.InputOutput";
                source.Append($@"            {parameter.Name}Parameter.Direction = {direction};
");
            }

            if (parameter.RefKind == RefKind.None || parameter.RefKind == RefKind.Ref)
            {
                if (requireParameterNullCheck)
                {
                    source.Append($@"            {parameter.Name}Parameter.Value = {parameter.Name} == null ? (object)DBNull.Value : {parameter.Name};
");
                }
                else
                {
                    source.Append($@"            {parameter.Name}Parameter.Value = {parameter.Name};
");
                }
            }
        }

        private string? ProcessClass(
            INamedTypeSymbol classSymbol,
            List<IMethodSymbol> methods,
            ISymbol attributeSymbol,
            bool hasNullableAnnotations)
        {
            if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
            {
                // TODO: issue a diagnostic that it must be top level
                return null;
            }

            string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            // begin building the generated source
            StringBuilder source = new StringBuilder($@"// <auto-generated>
// Code generated by Stored Procedures Code Generator.
// Changes may cause incorrect behavior and will be lost if the code is
// regenerated.
// </auto-generated>
#nullable enable
#pragma warning disable 1591

namespace {namespaceName}
{{
    using System;
    using System.Data.Common;
    using System.Linq;
    using Microsoft.EntityFrameworkCore;

    partial class {classSymbol.Name}
    {{
");

            // Create properties for each field
            foreach (IMethodSymbol methodSymbol in methods)
            {
                this.ProcessMethod(
                    source,
                    methodSymbol,
                    attributeSymbol,
                    hasNullableAnnotations);
            }

            source.Append(@"    }
}");
            return source.ToString();
        }

        private string GetConnectionStatement(INamedTypeSymbol classSymbol)
        {
            var connectionSymbol = GetConnectionField(classSymbol);
            if (connectionSymbol != null)
            {
                return $"var connection = this.{connectionSymbol.Name};";
            }

            var dbContextSymbol = GetContextField(classSymbol);
            var contextName = dbContextSymbol?.Name ?? "dbContext";
            return $"var connection = this.{contextName}.Database.GetDbConnection();";
        }

        private string GetOpenConnectionStatement(INamedTypeSymbol classSymbol)
        {
            var connectionSymbol = GetConnectionField(classSymbol);
            if (connectionSymbol != null)
            {
                return $"this.{connectionSymbol.Name}.Open();";
            }

            var dbContextSymbol = GetContextField(classSymbol);
            var contextName = dbContextSymbol?.Name ?? "dbContext";
            return $"this.{contextName}.Database.OpenConnection();";
        }

        private string GetCloseConnectionStatement(INamedTypeSymbol classSymbol)
        {
            var connectionSymbol = GetConnectionField(classSymbol);
            if (connectionSymbol != null)
            {
                return $"this.{connectionSymbol.Name}.Close();";
            }

            var dbContextSymbol = GetContextField(classSymbol);
            var contextName = dbContextSymbol?.Name ?? "dbContext";
            return $"this.{contextName}.Database.CloseConnection();";
        }

        private void ProcessMethod(
            StringBuilder source,
            IMethodSymbol methodSymbol,
            ISymbol attributeSymbol,
            bool hasNullableAnnotations)
        {
            // get the name and type of the field
            string fieldName = methodSymbol.Name;
            ITypeSymbol returnType = methodSymbol.ReturnType;
            var symbol = (ISymbol)methodSymbol;

            // get the AutoNotify attribute from the field, and any associated data
            AttributeData attributeData = methodSymbol.GetAttributes().Single(ad => ad.AttributeClass!.Equals(attributeSymbol, SymbolEqualityComparer.Default));
            TypedConstant overridenNameOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;
            var procedureName = attributeData.ConstructorArguments.ElementAtOrDefault(0);
            var signature = $"({string.Join(", ", methodSymbol.Parameters.Select(_ => GetParameterDeclaration(_)))})";
            var dbContextSymbol = GetContextField(methodSymbol.ContainingType);
            var contextName = dbContextSymbol?.Name ?? "dbContext";
            var itemType = GetUnderlyingType(returnType);
            var getConnection = this.GetConnectionStatement(methodSymbol.ContainingType);
            source.Append($@"        {GetAccessibility(symbol.DeclaredAccessibility)} partial {returnType} {methodSymbol.Name}{signature}
        {{
            {getConnection}
            using var command = connection.CreateCommand();

");
            if (methodSymbol.Parameters.Length > 0)
            {
                foreach (var parameter in methodSymbol.Parameters)
                {
                    DeclareParameter(source, hasNullableAnnotations, parameter);
                    source.Append($@"
");
                }

                source.Append($@"            var parameters = new DbParameter[]
            {{
");
                foreach (var parameter in methodSymbol.Parameters)
                {
                    source.Append($@"                {parameter.Name}Parameter,
");
                }

                source.Append(@"            };

");
            }

            if (methodSymbol.Parameters.Length == 0)
            {
                source.Append($@"            var sqlQuery = @""{procedureName.Value}"";
");
            }
            else
            {
                string parametersList = string.Join(", ", methodSymbol.Parameters.Select(parameter => GetParameterPassing(parameter)));
                source.Append($@"            var sqlQuery = @""{procedureName.Value} {parametersList}"";
");
            }

            var isList = itemType != returnType;
            if (IsScalarType(returnType))
            {
                source.Append($@"            command.CommandText = sqlQuery;
            command.Parameters.AddRange(parameters);
            {this.GetOpenConnectionStatement(methodSymbol.ContainingType)}
            try
            {{
                var result = command.ExecuteScalar();
                return ({returnType.ToDisplayString()})result;
            }}
            finally
            {{
                {this.GetCloseConnectionStatement(methodSymbol.ContainingType)}
            }}
");
            }
            else
            {
                var itemTypeProperty = GetDbSetField(dbContextSymbol, itemType)?.Name ?? itemType.Name + "s";
                source.Append($@"            var result = this.{contextName}.{itemTypeProperty}.FromSqlRaw(sqlQuery{(methodSymbol.Parameters.Length == 0 ? string.Empty : ", parameters")}).{(isList ? "ToList" : "AsEnumerable().FirstOrDefault")}();
");
                foreach (var parameter in methodSymbol.Parameters)
                {
                    var requireReadOutput = parameter.RefKind == RefKind.Out || parameter.RefKind == RefKind.Ref;
                    if (!requireReadOutput)
                    {
                        continue;
                    }

                    var requireParameterNullCheck = parameter.Type.CanHaveNullValue(hasNullableAnnotations);
                    if (requireParameterNullCheck)
                    {
                        source.Append($@"            {parameter.Name} = {parameter.Name}Parameter.Value == DbNull.Value ? ({parameter.Type.ToDisplayString()})null : ({parameter.Type.ToDisplayString()}){parameter.Name}Parameter.Value;
");
                    }
                    else
                    {
                        source.Append($@"            {parameter.Name} = ({parameter.Type.ToDisplayString()}){parameter.Name}Parameter.Value;
");
                    }
                }

                source.Append($@"            return result;
");
            }

            source.Append($@"        }}
");
        }

        internal class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<IMethodSymbol> Methods { get; } = new List<IMethodSymbol>();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                // any field with at least one attribute is a candidate for property generation
                if (context.Node is MethodDeclarationSyntax methodDeclarationSyntax
                    && methodDeclarationSyntax.AttributeLists.Count > 0)
                {
                    // Get the symbol being declared by the field, and keep it if its annotated
                    IMethodSymbol? methodSymbol = context.SemanticModel.GetDeclaredSymbol(context.Node) as IMethodSymbol;
                    if (methodSymbol == null)
                    {
                        return;
                    }

                    if (methodSymbol.GetAttributes().Any(ad => ad.AttributeClass?.ToDisplayString() == "StoredProcedureGeneratedAttribute"))
                    {
                        this.Methods.Add(methodSymbol);
                    }
                }
            }
        }
    }
}
