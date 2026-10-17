using BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace FoodRescue.Tests
{
    public sealed class ImageServiceTests : IDisposable
    {
        private readonly string _dir =
            Path.Combine(Path.GetTempPath(), "img-tests-" + Guid.NewGuid().ToString("N"));

        private ImageService Create() => new(_dir, NullLogger<ImageService>.Instance);

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private static async Task<byte[]> PngBytesAsync()
        {
            using var image = new Image<Rgba32>(4, 4);
            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            return ms.ToArray();
        }

        private static IFormFile File(byte[] bytes, string fileName, long? length = null) =>
            new FormFile(new MemoryStream(bytes), 0, length ?? bytes.Length, "image", fileName);

        [Fact]
        public async Task Save_ValidPng_ReturnsGuidFileName_AndWritesFile()
        {
            var name = await Create().Save(File(await PngBytesAsync(), "../../evil name.PNG"));

            Assert.Matches(new Regex("^[0-9a-f]{32}\\.png$"), name);
            Assert.True(System.IO.File.Exists(Path.Combine(_dir, name)));
        }

        [Fact]
        public async Task Save_IgnoresClientFileNameBaseName()
        {
            var name = await Create().Save(File(await PngBytesAsync(), "my-secret-photo.png"));
            Assert.DoesNotContain("secret", name);
        }

        [Theory]
        [InlineData("x.gif")]
        [InlineData("x.exe")]
        [InlineData("x.php")]
        [InlineData("x")]
        public async Task Save_DisallowedExtension_Throws_AndWritesNothing(string fileName)
        {
            await Assert.ThrowsAsync<ArgumentException>(
                async () => await Create().Save(File(await PngBytesAsync(), fileName)));

            Assert.Empty(Directory.EnumerateFiles(_dir));
        }

        [Fact]
        public async Task Save_TextFileDisguisedAsPng_Throws_AndLeavesNoOrphan()
        {
            var fake = Encoding.UTF8.GetBytes("<?php echo 1; ?>");

            await Assert.ThrowsAnyAsync<Exception>(async () => await Create().Save(File(fake, "x.png")));

            Assert.Empty(Directory.EnumerateFiles(_dir));
        }

        [Fact]
        public async Task Save_Over5MB_Throws()
        {
            var big = File(new byte[16], "x.png", length: 5 * 1024 * 1024 + 1);

            await Assert.ThrowsAsync<ArgumentException>(async () => await Create().Save(big));
            Assert.Empty(Directory.EnumerateFiles(_dir));
        }

        [Fact]
        public async Task Delete_RemovesExistingImage()
        {
            var service = Create();
            var name = await service.Save(File(await PngBytesAsync(), "a.png"));

            await service.Delete(name);

            Assert.False(System.IO.File.Exists(Path.Combine(_dir, name)));
        }

        [Fact]
        public async Task Delete_PathTraversal_DoesNotTouchFilesOutsideTheFolder()
        {
            var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".png");
            await System.IO.File.WriteAllBytesAsync(outside, new byte[] { 1 });

            try
            {
                var service = Create();
                await service.Delete("../" + Path.GetFileName(outside));
                await service.Delete(outside);

                Assert.True(System.IO.File.Exists(outside));
            }
            finally
            {
                System.IO.File.Delete(outside);
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-guid.png")]
        public async Task Delete_InvalidNames_AreIgnoredSilently(string? name)
        {
            await Create().Delete(name);
        }
    }
}
