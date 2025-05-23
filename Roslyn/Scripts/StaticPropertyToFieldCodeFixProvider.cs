﻿using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;

#pragma warning disable CS8604
#pragma warning disable CS8631
#pragma warning disable CS8602
#pragma warning disable RS1038
#pragma warning disable RS2008

// ReSharper disable ALL

namespace Herta.Roslyn
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StaticGetPropertyCodeFixProvider))]
    [Shared]
    internal sealed class StaticGetPropertyCodeFixProvider : CodeFixProvider
    {
        public override ImmutableArray<string> FixableDiagnosticIds => [StaticGetPropertyAnalyzer.DIAGNOSTIC_ID];

        public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

        public override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            Diagnostic diagnostic = context.Diagnostics[0];
            SyntaxNode? root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            PropertyDeclarationSyntax? property = root.FindNode(diagnostic.Location.SourceSpan) as PropertyDeclarationSyntax;
            if (property == null)
                return;
            context.RegisterCodeFix(CodeAction.Create("Convert to static readonly field", c => ConvertToFieldAsync(context.Document, property, c), "ConvertToField"), diagnostic);
        }

        private async Task<Document> ConvertToFieldAsync(Document document, PropertyDeclarationSyntax property, CancellationToken cancellationToken)
        {
            SyntaxGenerator generator = SyntaxGenerator.GetGenerator(document);
            string fieldName = property.Identifier.Text;
            TypeSyntax fieldType = property.Type;
            SyntaxNode fieldValue = generator.DefaultExpression(fieldType);
            if (property.ExpressionBody != null)
            {
                fieldValue = property.ExpressionBody.Expression;
            }
            else
            {
                AccessorDeclarationSyntax? getter = property.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                if (getter != null)
                {
                    if (getter.ExpressionBody != null)
                    {
                        fieldValue = getter.ExpressionBody.Expression;
                    }
                    else if (getter.Body != null)
                    {
                        ReturnStatementSyntax? returnStatement = getter.Body.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault();
                        if (returnStatement != null)
                            fieldValue = returnStatement.Expression ?? fieldValue;
                    }
                }
            }

            SyntaxNode fieldDeclaration = generator.FieldDeclaration(fieldName, fieldType, Accessibility.Public, DeclarationModifiers.Static | DeclarationModifiers.ReadOnly, fieldValue);
            SyntaxTriviaList leadingTrivia = property.GetLeadingTrivia();
            if (leadingTrivia.Any())
            {
                FieldDeclarationSyntax fieldSyntax = (FieldDeclarationSyntax)fieldDeclaration;
                fieldDeclaration = fieldSyntax.WithLeadingTrivia(leadingTrivia);
            }

            SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            SyntaxNode? newRoot = root.ReplaceNode(property, fieldDeclaration);
            return document.WithSyntaxRoot(newRoot);
        }
    }
}