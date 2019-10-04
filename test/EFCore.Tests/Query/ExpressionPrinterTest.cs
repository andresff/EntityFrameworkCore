// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Microsoft.EntityFrameworkCore.Query
{
    public class ExpressionPrinterTest
    {
        private readonly ExpressionPrinter _expressionPrinter = new ExpressionPrinter();

        [ConditionalFact]
        public void UnaryExpression_printed_correctly()
        {
            var expr1 = Expression.Convert(Expression.Constant(42), typeof(decimal));
            var expr2 = Expression.Throw(Expression.Constant("Some exception"));
            var expr3 = Expression.Not(Expression.Constant(true));
            var expr4 = Expression.TypeAs(Expression.Constant(new BaseClass()), typeof(DerivedClass));

            Assert.Equal("(decimal)42", _expressionPrinter.Print(expr1));
            Assert.Equal("throw \"Some exception\"", _expressionPrinter.Print(expr2));
            Assert.Equal("!(True)", _expressionPrinter.Print(expr3));
            Assert.Equal("(BaseClass as DerivedClass)", _expressionPrinter.Print(expr4));
        }

        private class BaseClass
        {
        }

        private class DerivedClass : BaseClass
        {
        }

        [ConditionalFact]
        public void BinaryExpression_printed_correctly()
        {
            var expr1 = Expression.MakeBinary(ExpressionType.Equal, Expression.Constant(7), Expression.Constant(42));
            var expr2 = Expression.MakeBinary(ExpressionType.NotEqual, Expression.Constant(7), Expression.Constant(42));
            var expr3 = Expression.MakeBinary(ExpressionType.GreaterThan, Expression.Constant(7), Expression.Constant(42));
            var expr4 = Expression.MakeBinary(ExpressionType.GreaterThanOrEqual, Expression.Constant(7), Expression.Constant(42));
            var expr5 = Expression.MakeBinary(ExpressionType.LessThan, Expression.Constant(7), Expression.Constant(42));
            var expr6 = Expression.MakeBinary(ExpressionType.LessThanOrEqual, Expression.Constant(7), Expression.Constant(42));
            var expr7 = Expression.MakeBinary(ExpressionType.AndAlso, Expression.Constant(true), Expression.Constant(true));
            var expr8 = Expression.MakeBinary(ExpressionType.OrElse, Expression.Constant(true), Expression.Constant(true));
            var expr9 = Expression.MakeBinary(ExpressionType.And, Expression.Constant(7), Expression.Constant(42));
            var expr10 = Expression.MakeBinary(ExpressionType.Or, Expression.Constant(7), Expression.Constant(42));
            var expr11 = Expression.MakeBinary(ExpressionType.ExclusiveOr, Expression.Constant(7), Expression.Constant(42));
            var expr12 = Expression.MakeBinary(ExpressionType.Add, Expression.Constant(7), Expression.Constant(42));
            var expr13 = Expression.MakeBinary(ExpressionType.Subtract, Expression.Constant(7), Expression.Constant(42));
            var expr14 = Expression.MakeBinary(ExpressionType.Multiply, Expression.Constant(7), Expression.Constant(42));
            var expr15 = Expression.MakeBinary(ExpressionType.Divide, Expression.Constant(7), Expression.Constant(42));
            var expr16 = Expression.MakeBinary(ExpressionType.Modulo, Expression.Constant(7), Expression.Constant(42));

            Assert.Equal("7 == 42", _expressionPrinter.Print(expr1));
            Assert.Equal("7 != 42", _expressionPrinter.Print(expr2));
            Assert.Equal("7 > 42", _expressionPrinter.Print(expr3));
            Assert.Equal("7 >= 42", _expressionPrinter.Print(expr4));
            Assert.Equal("7 < 42", _expressionPrinter.Print(expr5));
            Assert.Equal("7 <= 42", _expressionPrinter.Print(expr6));
            Assert.Equal("True && True", _expressionPrinter.Print(expr7));
            Assert.Equal("True || True", _expressionPrinter.Print(expr8));
            Assert.Equal("7 & 42", _expressionPrinter.Print(expr9));
            Assert.Equal("7 | 42", _expressionPrinter.Print(expr10));
            Assert.Equal("7 ^ 42", _expressionPrinter.Print(expr11));
            Assert.Equal("7 + 42", _expressionPrinter.Print(expr12));
            Assert.Equal("7 - 42", _expressionPrinter.Print(expr13));
            Assert.Equal("7 * 42", _expressionPrinter.Print(expr14));
            Assert.Equal("7 / 42", _expressionPrinter.Print(expr15));
            Assert.Equal("7 % 42", _expressionPrinter.Print(expr16));
        }

        [ConditionalFact]
        public void ConditionalExpression_printed_correctly()
        {
            var expr = Expression.Condition(
                Expression.Constant(true),
                Expression.Constant("Foo"),
                Expression.Constant("Bar"));

            Assert.Equal("True ? \"Foo\" : \"Bar\"", _expressionPrinter.Print(expr));
        }

        [ConditionalFact]
        public void Simple_lambda_printed_correctly()
        {
            var expr = Expression.Lambda(
                Expression.Constant(42),
                Expression.Parameter(typeof(int), "prm"));

            Assert.Equal("prm => 42", _expressionPrinter.Print(expr));
        }

        [ConditionalFact]
        public void Multi_parameter_lambda_printed_correctly()
        {
            var expr = Expression.Lambda(
                Expression.Constant(42),
                Expression.Parameter(typeof(int), "prm1"),
                Expression.Parameter(typeof(int), "prm2"));

            Assert.Equal("(prm1, prm2) => 42", _expressionPrinter.Print(expr));
        }

        [ConditionalFact]
        public void Unhandled_parameter_in_lambda_detected()
        {
            var expr = Expression.Lambda(
                Expression.Parameter(typeof(int), "prm2"),
                Expression.Parameter(typeof(int), "prm1"));

            Assert.Equal("prm1 => (Unhandled parameter: prm2)", _expressionPrinter.Print(expr));
        }

        [ConditionalFact]
        public void MemberAccess_after_BinaryExpression_adds_parentheses()
        {
            var expr = Expression.Property(
                Expression.Add(
                    Expression.Constant(7, typeof(int?)),
                    Expression.Constant(42, typeof(int?))),
                "Value");

            Assert.Equal(@"(7 + 42).Value", _expressionPrinter.Print(expr));
        }

        [ConditionalFact]
        public void Simple_MethodCall_printed_correctly()
        {
            var expr = Expression.Call(
                Expression.Constant("Foo"),
                typeof(string).GetMethods().Single(m => m.Name == nameof(string.ToUpper) && m.GetParameters().Count() == 0));

            Assert.Equal(@"""Foo"".ToUpper()", _expressionPrinter.Print(expr));
        }

        [ConditionalFact]
        public void Complex_MethodCall_printed_correctly()
        {
            var expr = Expression.Call(
                Expression.Constant("Foobar"),
                typeof(string).GetMethods().Single(m => m.Name == nameof(string.Substring) && m.GetParameters().Count() == 2),
                Expression.Constant(0),
                Expression.Constant(4));

            Assert.Equal(@"""Foobar"".Substring(
    startIndex: 0, 
    length: 4)", _expressionPrinter.Print(expr));
        }

        [ConditionalFact]
        public void Linq_methods_printed_as_extensions()
        {
            Expression<Func<object, object>> expr =
                _ => new[] { 1, 2, 3 }.AsQueryable().Select(x => x.ToString()).AsEnumerable().Where(x => x.Length > 1);

            Assert.Equal(
                @"new int[]
{ 
    1, 
    2, 
    3 
}
    .AsQueryable()
    .Select(x => x.ToString())
    .AsEnumerable()
    .Where(x => x.Length > 1)",
                _expressionPrinter.Print(expr.Body));
        }

        [ConditionalFact]
        public void Use_Print_method_when_printing_extension_expression()
        {
            var expr = new NullConditionalExpression(
                Expression.Constant("caller"),
                Expression.Constant("accessOperation"));

            Assert.Equal(expr.Print(), _expressionPrinter.Print(expr));
        }
    }
}
