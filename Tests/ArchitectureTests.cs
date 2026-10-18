using BLL.Services;
using DAL;
using FoodRescue.Controllers;
using FoodRescue.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Xunit;

namespace FoodRescue.Tests
{
    // בדיקות מבנה לפי מדריך "ההבדל בין Repository ל-Service":
    // Controller -> Service -> Repository -> DbContext, וכל שכבה מכירה רק את מה שמתחתיה.
    public class ArchitectureTests
    {
        private static readonly Assembly Api = typeof(ApiControllerBase).Assembly;
        private static readonly Assembly Bll = typeof(DonationService).Assembly;
        private static readonly Assembly Dal = typeof(FoodRescueDbContext).Assembly;
        private static readonly Assembly Dtos = typeof(PageRequest).Assembly;

        // כל הטיפוסים שמופיעים בחתימה (כולל ארגומנטים גנריים, מערכים ו-Task<T>)
        private static IEnumerable<Type> Flatten(Type type)
        {
            yield return type;
            if (type.HasElementType)
                foreach (var t in Flatten(type.GetElementType()!)) yield return t;
            if (type.IsGenericType)
                foreach (var arg in type.GetGenericArguments())
                    foreach (var t in Flatten(arg)) yield return t;
        }

        private static IEnumerable<Type> SignatureTypes(Type type)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var ctor in type.GetConstructors(flags))
                foreach (var p in ctor.GetParameters())
                    yield return p.ParameterType;

            foreach (var method in type.GetMethods(flags).Where(m => !m.IsSpecialName))
            {
                yield return method.ReturnType;
                foreach (var p in method.GetParameters())
                    yield return p.ParameterType;
            }

            foreach (var field in type.GetFields(flags))
                yield return field.FieldType;

            foreach (var prop in type.GetProperties(flags))
                yield return prop.PropertyType;
        }

        private static List<string> DalTypesUsedBy(IEnumerable<Type> types) =>
            types
                .SelectMany(t => SignatureTypes(t).SelectMany(Flatten).Select(used => (Owner: t, Used: used)))
                .Where(x => x.Used.Assembly == Dal)
                .Select(x => $"{x.Owner.Name} -> {x.Used.Name}")
                .Distinct()
                .ToList();

        private static IEnumerable<Type> ApiTypes(string ns) =>
            Api.GetTypes().Where(t => t.Namespace != null && t.Namespace.StartsWith(ns, StringComparison.Ordinal) && !t.IsNested);

        [Fact]
        public void Controllers_Hubs_AndSecurity_DoNotUseDalTypes()
        {
            var types = ApiTypes("FoodRescue.Controllers")
                .Concat(ApiTypes("FoodRescue.Hubs"))
                .Concat(ApiTypes("FoodRescue.Security"))
                .Concat(ApiTypes("FoodRescue.Extensions"));

            var violations = DalTypesUsedBy(types);
            Assert.True(violations.Count == 0, "API types that use DAL types: " + string.Join(", ", violations));
        }

        [Fact]
        public void ServiceInterfacesUsedByControllers_ExposeOnlyDtos()
        {
            var serviceInterfaces = ApiTypes("FoodRescue.Controllers")
                .Concat(ApiTypes("FoodRescue.Hubs"))
                .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()))
                .Select(p => p.ParameterType)
                .Where(t => t.IsInterface && t.Assembly == Bll)
                .Distinct()
                .ToList();

            Assert.NotEmpty(serviceInterfaces);

            var violations = serviceInterfaces
                .SelectMany(i => i.GetMethods().SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
                    .SelectMany(Flatten)
                    .Where(t => t.Assembly == Dal)
                    .Select(t => $"{i.Name} -> {t.Name}"))
                .Distinct()
                .ToList();

            Assert.True(violations.Count == 0, "Service interfaces that expose DAL types: " + string.Join(", ", violations));
        }

        [Fact]
        public void Services_DoNotUseTheDbContextDirectly()
        {
            var violations = Bll.GetTypes()
                .Where(t => SignatureTypes(t).SelectMany(Flatten).Any(u => typeof(Microsoft.EntityFrameworkCore.DbContext).IsAssignableFrom(u)))
                .Select(t => t.FullName)
                .ToList();

            Assert.True(violations.Count == 0, "BLL types that use a DbContext: " + string.Join(", ", violations));
        }

        [Fact]
        public void Dal_DoesNotKnowDtosOrBll()
        {
            var referenced = Dal.GetReferencedAssemblies().Select(a => a.Name).ToList();

            Assert.DoesNotContain(Dtos.GetName().Name, referenced);
            Assert.DoesNotContain(Bll.GetName().Name, referenced);
        }
    }
}
