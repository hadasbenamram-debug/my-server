using AutoMapper;
using BLL.Exceptions;
using BLL.Services;
using FoodRescue.Controllers;
using FoodRescue.DTOs;
using DAL;
using DAL.Classes;
using DAL.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    public class RegionControllerTests
    {
        private static FoodRescueDbContext NewContext() =>
            new(new DbContextOptionsBuilder<FoodRescueDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);

        // כל השכבות אמיתיות: Controller -> Service -> Repository -> DbContext (בזיכרון)
        private static RegionController NewController(FoodRescueDbContext ctx) =>
            new(new RegionService(
                new RegionRepository(ctx),
                new NgoRepository(ctx),
                new DonationRepository(ctx),
                new AvailabilitySlotRepository(ctx),
                TestDb.Mapper,
                NullLogger<RegionService>.Instance));

        // ----- authorization metadata -----

        [Fact]
        public void Class_RequiresAuthentication()
        {
            Assert.NotNull(typeof(RegionController).GetCustomAttribute<AuthorizeAttribute>());
        }

        [Fact]
        public void Delete_IsAdminOnly_AndNotAnonymous()
        {
            var method = typeof(RegionController).GetMethod(nameof(RegionController.DeleteRegion))!;

            Assert.Equal("Admin", method.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
            Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
        }

        [Theory]
        [InlineData(nameof(RegionController.GetAllRegions))]
        [InlineData(nameof(RegionController.GetRegionById))]
        public void Reads_AreExplicitlyAnonymous(string name)
        {
            var method = typeof(RegionController).GetMethod(name)!;
            Assert.NotNull(method.GetCustomAttribute<AllowAnonymousAttribute>());
        }

        // ----- behaviour -----

        // אין יותר try/catch בקונטרולר: החריגה מתורגמת ל-404 ב-ExceptionHandlingMiddleware
        [Fact]
        public async Task Delete_MissingRegion_IsNotFound()
        {
            using var ctx = NewContext();
            await Assert.ThrowsAsync<NotFoundException>(() => NewController(ctx).DeleteRegion(123));
        }

        [Fact]
        public async Task Delete_UnusedRegion_ReturnsNoContent_AndRemovesIt()
        {
            using var ctx = NewContext();
            ctx.Regions.Add(new Region { Id = 1, RegionName = "North" });
            await ctx.SaveChangesAsync();

            var result = await NewController(ctx).DeleteRegion(1);

            Assert.IsType<NoContentResult>(result);
            Assert.False(await ctx.Regions.AnyAsync());
        }

        [Fact]
        public async Task Delete_RegionUsedByNgo_IsConflict_NotAnUnhandledError()
        {
            using var ctx = NewContext();
            ctx.Regions.Add(new Region { Id = 1, RegionName = "North" });
            ctx.Ngos.Add(new Ngo
            {
                Id = 1, UserId = 1, NgoName = "N", DailyCapacity = 1, Address = "A",
                GooglePlaceId = "N/A", RegistrationNumber = "1", RegionId = 1
            });
            await ctx.SaveChangesAsync();

            await Assert.ThrowsAsync<ConflictException>(() => NewController(ctx).DeleteRegion(1));
            Assert.True(await ctx.Regions.AnyAsync());
        }

        // תוקן: הרשימה החזירה DTO בלי Id, ולכן אי אפשר היה לבחור אזור
        [Fact]
        public async Task GetAll_And_Add_ReturnTheId()
        {
            using var ctx = NewContext();
            var controller = NewController(ctx);

            var added = (await controller.AddRegion(new CreateRegionDto { RegionName = " צפון ", CenterLatitude = 32.8, CenterLongitude = 35.0, RadiusKm = 30 })).Result as OkObjectResult;
            var addedDto = Assert.IsType<RegionResponseDto>(added!.Value);
            Assert.True(addedDto.Id > 0);
            Assert.Equal("צפון", addedDto.RegionName);

            var list = (await controller.GetAllRegions()).Result as OkObjectResult;
            var item = Assert.Single((System.Collections.Generic.IEnumerable<RegionResponseDto>)list!.Value!);
            Assert.Equal(addedDto.Id, item.Id);
        }

        [Fact]
        public void Add_IsAdminOnly()
        {
            var method = typeof(RegionController).GetMethod(nameof(RegionController.AddRegion))!;
            Assert.Equal("Admin", method.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
        }
    }
}
