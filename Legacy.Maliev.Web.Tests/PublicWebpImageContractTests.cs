using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Legacy.Maliev.Web.Tests;

public sealed partial class PublicWebpImageContractTests
{
    [Fact]
    public void SourceDerivedWebpAssets_MatchCommit6509356()
    {
        var expected = new Dictionary<string, (string Sha256, long Bytes)>(StringComparer.Ordinal)
        {
            ["apple-touch-icon.webp"] = ("0f4444eb5c23dcaae9f9c99ec4eb6b102fb8e07d979d61b7a01a77d92783552e", 3762),
            ["favicon-16x16.webp"] = ("d3bb9eddb84df6c8014e335e317399bdb80956844563e62c36d5e09a692948ea", 252),
            ["favicon-32x32.webp"] = ("59366423e121f91259ac8099ffb71839f4fe551ab1b800bf7fa1d98611726e2f", 612),
            ["favicon.webp"] = ("4873940545d64653135bcbf081bc8ed0ea773c618b75a7f9c63c8207ce74a936", 13516),
            ["src/images/account-forget-password.webp"] = ("391a5f400b411047f72e73bd6bc11e6fb1f7d9d1818418f49cdc37c4c4d59f51", 473786),
            ["src/images/account-signin.webp"] = ("6176aae040747d50df0f041a134fa55f837655d72f97d1764f3ef9b102046e13", 342852),
            ["src/images/account-signup.webp"] = ("0f5258766cd4af63c6dcac7c8ac7d245d2e708567b9f60f57ed721b139cc6db2", 76902),
            ["src/images/career-3dprinting.webp"] = ("3e42ddefbe2bff8fddbb8034aa59ea3de785ac68fdc524b19c73b4bc5f87b90f", 213822),
            ["src/images/Flags/sprite-flags.webp"] = ("db79ee0c2fc81438881a55381c09592f1be4904aec1b8e54677a373a502f885d", 780),
            ["src/images/f-ogo_RGB_HEX-58.webp"] = ("814f12d52cb054756af99429ecf478c8e1488be650a70a3e8456b56f61237fec", 234),
            ["src/images/homepage-cover-1.webp"] = ("bf3df298ff398b9a0ed82f729820dc02774da89bbb65b427c9a5c6bf12e69341", 72548),
            ["src/images/navbar_logo_black.webp"] = ("57185bb19cd458ab670b1e347dda9e3cec4c2dc6b1a7cc1f3ecfb40e429d0236", 3430),
            ["src/images/Payments/sprite-payments.webp"] = ("8453081f6794634f1330cbbb1a3dd95bedc092e02d8957868ac8f71f9645807b", 4172),
            ["src/images/ShippingCouriers/sprite-shippingcouriers.webp"] = ("aa6b8ebc2ea4f853c16767c81ed3fd05b282df0bf43f17ad2cc52557b896e1e5", 2260),
            ["src/images/services/injection-molding/injection-service-hero.webp"] = ("919c8ba58a49680f0bc04854f034cc283f6170ecd08d1e56b61362004d0766eb", 65768),
            ["src/images/304-cnc-machining.webp"] = ("5c9d31f75a612afee74e44405626da8947f38e4c77216d92950cd2debf2c3967", 7078),
            ["src/images/3d-printing-cover-expanded-1.webp"] = ("654afa10ef80cc947cc3da899d1ac9e87ad978e01a8e03836e922052d2d0fa7f", 7828),
            ["src/images/3d-printing-cover-expanded-2.webp"] = ("2a99b29c16b0ee76a2f479a11a7b84a0ec5038702735daf5c99eeb2c565c9efa", 14638),
            ["src/images/3d-printing-cover-expanded-3.webp"] = ("6e6513f9e8f2b73159639a6d6e6f1353e629ca7f90d28be19e6e8349b0a399f7", 18348),
            ["src/images/3d-scanner-banner-1.webp"] = ("481411d27e6effc538eb1e445ae24c8124b53087340dc5cd1d64c8f3bdc77a95", 116404),
            ["src/images/3d-scanning-cover-expanded-1.webp"] = ("a8b4f64c080af04e0586cb542af91edd74073de1b2c6a7e062ebd8fc9c54e725", 15676),
            ["src/images/3d-scanning-cover-expanded-2.webp"] = ("aebb2eb526b6d1e00954f3858e60714d84d8155bfb471fbf62c9a27fef96375b", 35144),
            ["src/images/5052-cnc-machining.webp"] = ("82576deaf8997c3dead78b85fb4698184e9f02a9a045b42f03d04f92195ac65c", 6770),
            ["src/images/6061-cnc-machining.webp"] = ("c814f407c91ab0d421686c28ddbdc4f58cccf38b32033301abfdd4d7a7f05d88", 6448),
            ["src/images/7075-cnc-machining.webp"] = ("f4e79f36958bf708813363b5ac96189ed73fd77e3ad059e576cdf4009bfbae82", 7056),
            ["src/images/abs-3d-printing.webp"] = ("be856a32a813dd8c0c0128434721bb887a03a71a3246a1f99a6e01a37c67cb6a", 13800),
            ["src/images/asa-3d-printing.webp"] = ("158e96b58f9df7b6a2a92567e3791f3dcbe9f6b62ba35876f9b66453e5510fa5", 23650),
            ["src/images/box-dimensions-annotation.webp"] = ("83fc8bf946f1d6ea58695816cfa0871222838e692a1f970b5d6285062018c309", 4402),
            ["src/images/brass-cnc-machining.webp"] = ("535f2d2e7a8015a893e5cce4c752db468cbb48d9de8596cf7292aa46ad7779f4", 5786),
            ["src/images/career-heading.webp"] = ("7bc99e8cd5e474e1121d2549eca0c087bd02a8cd0cd2e98b446926cf82033932", 204954),
            ["src/images/cnc-machining-cover-expanded-1.webp"] = ("bff84de45359d6d74daf7e54301012bcc64b01df71f8ae7377ce7da0b0edbeff", 13942),
            ["src/images/cnc-machining-cover-expanded-2.webp"] = ("94494587a289578d437ce5539e60de8e0f918f067269d3820076758f22849424", 17206),
            ["src/images/cnc-machining-cover-expanded-3.webp"] = ("e6a0fd6d4f284b2abb428dec8e4099202d529ccc658227ac9ee95bdf50ce9093", 11444),
            ["src/images/copper-cnc-machining.webp"] = ("32279734aafcad5d9813a58ac777a8eb936e8b77bca12bc5ecf593d8bf61034b", 8870),
            ["src/images/fdm-printing.webp"] = ("eab718204ec42565583cab70fa325290a1f5b1d25dfaf36a14904e141df2d5f4", 13572),
            ["src/images/Flags/thailand.webp"] = ("5ebb25a19bdbfd303949bc47d4a5b5f8f38705542936f1f93c3bc49b5970ad6c", 284),
            ["src/images/Flags/united-states.webp"] = ("abcf6f3491598ff4bb2aefeeda5e4ab48dc8d82ba8d852a6d53d51191bb57308", 582),
            ["src/images/hips-3d-printing.webp"] = ("19542910fd086812f067e348d49c113c53b39bfcb7ad39bf49e1775ea005f6b7", 32098),
            ["src/images/inexpensive-rp-hero-background.webp"] = ("d93218b56e401752e556cb9003f8dd534dc8d06ad9447f584dadbb685d5afa11", 255910),
            ["src/images/instagram-logo.webp"] = ("27f26e16581851359d8fa5c7ee188678e6d1e2aab450896bb4bcb25099479957", 998),
            ["src/images/Knowledges/ANSI Features/Concentricity.webp"] = ("7d49ed16cda3142197525cbdd9bd0cf56240858afd48996321d7905accdfa149", 888),
            ["src/images/Knowledges/ANSI Features/Diameter.webp"] = ("4b4827b989f2ce94c4021af034818016a02051651caa420057f86ceb61f94843", 494),
            ["src/images/Knowledges/ANSI Features/Parallelism.webp"] = ("34b0d7bbf54902787f48662f1f0d09da228776c85af0082c657cf6e55aa13f2c", 704),
            ["src/images/Knowledges/ANSI Features/Position.webp"] = ("7089a82f8541d159b66de16e5fd0682a73360b2b05072cf340476512373ad391", 748),
            ["src/images/Knowledges/ANSI Features/Profile.webp"] = ("1d66725701acbad106f8c14c9418e20b62bd58fcb61e444cd28a01056ea6a31b", 746),
            ["src/images/Knowledges/ANSI Features/Roundness.webp"] = ("1847d2d9442cbe588c9e603bbf0013ecc82181c93eb35c481afff214214407a5", 482),
            ["src/images/Knowledges/ANSI Features/Runout.webp"] = ("1c51bc0e3e7096dbe15fb4b8d253607b3470d25f38599851dbb827d8af1668f0", 692),
            ["src/images/landing/service-directory-finder.webp"] = ("fc38137cca727a125e0e788ef81a68abf122b737a4cd6f806963e480d0c894f4", 40350),
            ["src/images/line-logo.webp"] = ("e7fae41333bcff409827625ea6affd9bdc35fe8d4f7738044d91bfd47a00b5ff", 12780),
            ["src/images/line-qr-clean.webp"] = ("aa52dabf4a50afde784ee86ef2d7e711b4f77597225dcd69cc5ba81cd2b39bf9", 1610),
            ["src/images/line-qr.webp"] = ("63dbe6876f753ba832665c66376b72905f91a14923c2bfe5c6fe1fccf4446212", 19534),
            ["src/images/mjf-printing.webp"] = ("bc0605610aa99f8e9b25c79f629f129a1ef6a222b9da5d5f1f7fc71d7fe347de", 17910),
            ["src/images/multicolor-3d-printing.webp"] = ("4f18be208e6ebfc9690468ceb7069108724ed57e59f80f47ea34f4a5c21bafd5", 15852),
            ["src/images/natthakarn-vanasrivilai-profile.webp"] = ("cc46256db1dbd7a56efa973f1463fb55b460a92bfd4920510fe0d267da458d63", 5792),
            ["src/images/navbar_logo_white.webp"] = ("18ecf9cb22c394c3b788428342b6c9d6fb12d77fcc2904f444ddcc3057a02f23", 868),
            ["src/images/opengraph/og-image-1.webp"] = ("6fc1c6b352e1d2fc01ba8b92c56d435deb196f06cbb5138217f79173a2b73bca", 22436),
            ["src/images/opengraph/og-image-2.webp"] = ("40d81a209eb9f0d444adaadc459233b5b9d745fdae24706cbd47809333193151", 21706),
            ["src/images/opengraph/og-image-3.webp"] = ("42c4b3a16fe31f98b0a2e5a60a232f427e0414e21c01fac8038343418ba14e93", 18948),
            ["src/images/opengraph/og-image-4.webp"] = ("e73e0e7ceec95b015d59c2885d95a0f5cde789fb1ca780b3263a3a60c379ca37", 323366),
            ["src/images/opengraph/og-image-5.webp"] = ("08baee2487f50435ebaecfb20f0fdf66179c9b0c5ebadd33afdb0b8e1f0c8650", 62760),
            ["src/images/opengraph/og-image.webp"] = ("424588dce15765a315491c252499dd0b6f28a60495c75935965a176dc1af286d", 950),
            ["src/images/pa12-3d-printing.webp"] = ("2f02ae35f6905f8fbcce21879635caa54f8c9ccc0159b7dea533ba4aef84040c", 10798),
            ["src/images/pa6-3d-printing.webp"] = ("73da1d326cbf3acb9cf912f0bc83352a2a089a77bf22dc54ae4f413e3c158feb", 10526),
            ["src/images/pacf-3d-printing.webp"] = ("3c423e6d7e0fbc030d34ca3274be7f2345473861c0ceae0892d8c898e91dbfe0", 6522),
            ["src/images/Payments/logo-bangkokbank.webp"] = ("ebd0f6538deb603a4ebe825aeb98f78986708cdf29256ee56efb6a1580f0b252", 340),
            ["src/images/Payments/logo-cash.webp"] = ("184badf3a200ba317309ea7276b6eaf7329d7e849f2d4c466e0332dd7056bb81", 542),
            ["src/images/Payments/logo-kasikornbank.webp"] = ("0773a7d6ff535e0a71b92e4287185af5dce771af95ac24012668125f46e180a4", 404),
            ["src/images/Payments/logo-krungsribank.webp"] = ("5fbcac6cb3c5ef3f59eb24f08ae22a202bf6beb72fb030abeba29d07d946ca4a", 342),
            ["src/images/Payments/logo-krungthaibank.webp"] = ("4c463ae13f79fd5ad12090259376a1e75e033d8849638449406a8c56ffdb68b4", 316),
            ["src/images/Payments/logo-mastercard.webp"] = ("01256842095c85e68a7a184e566771d746224151371b4cf4f67be7f514eec055", 388),
            ["src/images/Payments/logo-paypal.webp"] = ("222871553308f2c0c196c8c532215dc76e1e9f9fc1e9b87da5ecd0e5a674109f", 378),
            ["src/images/Payments/logo-scbbank.webp"] = ("b21b8ec8f73d8eafb74d245fe3a34d0f271b6a5e9c9b5b69302e10c272cc2130", 348),
            ["src/images/Payments/logo-thanachartbank.webp"] = ("d28cdf1f87e3cfbe274c4c23a5fe7eee61c980e729ba224defcb1d5dd32ad524", 316),
            ["src/images/Payments/logo-tmbbank.webp"] = ("fe3040824e076d72efaf23992a563e8ce0e9c913ef6dd5c4ec65fae1308b73c6", 362),
            ["src/images/Payments/logo-visa.webp"] = ("b6d7672f420a6f9f4b15341892f5ebfcde0462d01be0d31e6db256c9d9796f38", 336),
            ["src/images/pc-3d-printing.webp"] = ("670ecd7fd4c77cc7618d9d39198fb84436a5ffc87b12746adfbdd1b1ebb513ce", 2754),
            ["src/images/petg-3d-printing.webp"] = ("13341264b5694a477694ed9f4466b97caa507685d1d79baf73c2f8d65f296cf6", 21308),
            ["src/images/pla-3d-printing.webp"] = ("77bf9d46d7da3bf73b6bc3ec4063549f7558bc19b2dfd16ee731d158b563f3de", 7698),
            ["src/images/pva-3d-printing.webp"] = ("9f8974f1b5b47adb187ab61dbeb9342f07a878f54d2b3fbc331a4f1e5503e121", 10194),
            ["src/images/resin-3d-printing.webp"] = ("6b20076aa92d3560cf2a21a4f654008bd9e5704d8a9eb6c05a6c11af16f78fff", 9364),
            ["src/images/s50c-cnc-machining.webp"] = ("8fa27347ce46895d256e32a6beeea3acd0c4e4442be0c6d76038ec98705536a1", 6244),
            ["src/images/scm440-cnc-machining.webp"] = ("9aecac2c50dd2508dd4bfaccf92a66934bd272e9677a5d76406ecffb2e0c592e", 4302),
            ["src/images/services/injection-molding/injection-card-workshop.webp"] = ("c40e71a564534daeb01d269d61dad01ccc9f40a6320d413a562c0b8112150aaf", 87284),
            ["src/images/ShippingCouriers/logo-dhlexpress.webp"] = ("b3c753afec89ca25e1b95f3e46685321e91647fa6bc07a2a7e452e95775eb602", 324),
            ["src/images/ShippingCouriers/logo-dhl.webp"] = ("555d4fb57155e1dba1b153c33da9965b6bc90b6e8de41aa0fdf55a4a9079f8c6", 316),
            ["src/images/ShippingCouriers/logo-grabexpress.webp"] = ("6529ab71e8edb704320715578e984d53c4599ef11bb6285b409a03a03eb73fe0", 370),
            ["src/images/ShippingCouriers/logo-kerryexpress.webp"] = ("a5b0f419abe03d87e126e6b9b931a71bef68e6d3f5bbaade2732231f9ba36d27", 284),
            ["src/images/ShippingCouriers/logo-lalamove.webp"] = ("efd7a218a31fbb361bea0be8cbd74fc7d91bc0b10e8df1c60da2f2bf9fd97176", 310),
            ["src/images/ShippingCouriers/logo-lineman.webp"] = ("6cf8ebcb33597856f187320596354c24b183e1f03dae1802929f0d30c5e3d0aa", 314),
            ["src/images/ShippingCouriers/logo-thailandpost.webp"] = ("4d1f9acaf3485187eda0c6e8ea1cccdfbbe2363d73f5d57e45dde6c160ceca6e", 406),
            ["src/images/ShippingCouriers/logo-ups.webp"] = ("2ea20e766aff8c1a9a36567e505888fc3caf3d9e6ee9bfb95112e7fcbf2bfbf8", 312),
            ["src/images/sla-printing-soon.webp"] = ("5a17a063381a1c9f01fff9778de5b18a8a3b4b51da27cb979db0e125b5e42ed0", 11498),
            ["src/images/sla-printing.webp"] = ("44ee5775c24d3782df2d106521679db566a57fcb6f0b36458f43eb31e4c37fa2", 11468),
            ["src/images/sls-printing-soon.webp"] = ("a7a77171725720c80e97f7a357edbacf74f18c82af027b858febeef908f0c341", 7746),
            ["src/images/sls-printing.webp"] = ("0ec42b0d1f925768ec22abd39c16e061478c4d4bafd2bdbb58a98930d56a4dba", 23686),
            ["src/images/tpu-3d-printing.webp"] = ("2e5702db556d06683d2cf1e9155ed1cd6a35eb75dc54c36e00f5ad1954b62dfc", 13264),
        };

        string webRoot = Path.Combine(FindRepositoryRoot(), "Legacy.Maliev.Web", "wwwroot");
        foreach ((string relativePath, (string sha256, long bytes)) in expected)
        {
            string path = Path.Combine(webRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing source-derived WebP asset: {relativePath}");
            Assert.Equal(bytes, new FileInfo(path).Length);
            Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }
    }

    [Fact]
    public void PublicRuntimeSources_ReferenceOnlyWebpRasterImages()
    {
        string root = FindRepositoryRoot();
        string webRoot = Path.Combine(root, "Legacy.Maliev.Web");
        string[] sourceRoots =
        [
            Path.Combine(webRoot, "Components"),
            Path.Combine(webRoot, "Pages"),
            Path.Combine(webRoot, "Areas"),
            Path.Combine(webRoot, "wwwroot", "src", "app"),
            Path.Combine(webRoot, "wwwroot", "src", "data"),
        ];

        string[] sourceFiles = sourceRoots
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".razor" or ".cshtml" or ".cs" or ".css" or ".js" or ".mjs" or ".json")
            .Append(Path.Combine(webRoot, "Components", "App.razor"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Final source 770cb580 retains these later customer-logo originals as source inputs.
        // Freeze exact files and bytes; every other browser-served raster must be WebP.
        var retainedSourceInputs = new Dictionary<string, (string Sha256, long Bytes)>(StringComparer.Ordinal)
        {
            ["src/images/customers/crown-seal.png"] = ("8796dce4263902d6633810f0e510f66636ebecf6ad6f7e0bc644a9f3557f9f68", 9426),
            ["src/images/customers/fabrinet.png"] = ("379453f77d9b8eede9bcf8151372cf632193ba4b9287eceb86fcb11f5616501b", 8209),
            ["src/images/customers/khon-kaen-sugar.png"] = ("7abfb51c3ce6ebc058cc0e0d854beddd83743f41b70a800aac9913c97236fe96", 4559),
            ["src/images/customers/kobelco.gif"] = ("82963050c2b450c166a2e6f98c16dd2a41e50a9a5da8dd1353998b810856546f", 2802),
            ["src/images/customers/kohler.png"] = ("3e3572e43a10e72ac4071893c30fd019f470fa84b31ea02dc25c6bee34eb9b47", 2539),
            ["src/images/customers/mektec.png"] = ("42aeb7fb11e9aacf799d9d38cf9a23204e242378e3f370faea8d01d026f64253", 7447),
            ["src/images/customers/mfec.png"] = ("96d6de2772333c670d0a022cbd7239f763b9280a01936b5aefa20925d43e5a15", 22572),
            ["src/images/customers/minor-food.png"] = ("56fd7fabf3cdc29f8f64419dcb3b394f466e67bd1289e0684c9627e4f4d53dce", 39709),
            ["src/images/customers/mitsubishi-materials.png"] = ("c761ad949ef9e2bed73b99a1bd3886ee3576260daa5b00db6d47a0a66c1d7fa8", 8051),
            ["src/images/customers/olympus.png"] = ("2f89895cda9d845705cd87e9467d3fda719f1d41cc00f720193ea9ebb4e75591", 1925),
            ["src/images/customers/ptt.png"] = ("a800ec46765bafee5692624f8c7f8c6efd6d6a818784b740c21fcffafa4fcf76", 22718),
            ["src/images/customers/singha-kameda-current.png"] = ("5ef06d8d337f72b9c03904cf080c7615183345adafe9357c64ca97d12b26e753", 38923),
            ["src/images/customers/sp-syndicate.jpg"] = ("d5226d3b69e8cd84bc8380ed3b2e5888aafdf4af682e0baf9e9a8faabe83096c", 3415),
            ["src/images/customers/thai-asahi-denso-en.png"] = ("2222f2166ac130ae80a202edd5e823b2d19a4caef5a7ae4e080462f550676088", 3403),
            ["src/images/customers/thai-coconut.jpg"] = ("dc1fbb9e8dbfdbdedbe4637ae7860f76bedad30189751c3a183042ef2f33f9a5", 35071),
            ["src/images/customers/thai-optical.png"] = ("2c0265a1bad345f534ed3fd86659608e223ce9cf19ea789fa72fcb6156746dfc", 6809),
            ["src/images/customers/tpi-polene-power-emblem.png"] = ("e3f5687098b53f137e4ed9bac25bba62deba91404fea50a0b360c92d186e9535", 6041),
            ["src/images/customers/yg-1-official-mark.png"] = ("75e7f962223eca5f3a4405adef091249c26d034d02b7c9aa59802ea765bdb1db", 7399),
        };
        string assetRoot = Path.Combine(webRoot, "wwwroot");
        foreach ((string relativePath, (string sha256, long bytes)) in retainedSourceInputs)
        {
            string path = Path.Combine(assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Missing retained final-source input: {relativePath}");
            Assert.Equal(bytes, new FileInfo(path).Length);
            Assert.Equal(sha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }
        string[] forbiddenExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tif", ".tiff", ".avif", ".ico"];
        string[] nonWebpAssets = Directory.EnumerateFiles(assetRoot, "*", SearchOption.AllDirectories)
            .Where(path => forbiddenExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(assetRoot, path).Replace('\\', '/'))
            .Where(path => !retainedSourceInputs.ContainsKey(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.True(nonWebpAssets.Length == 0, "Unexpected non-WebP assets: " + string.Join(", ", nonWebpAssets));

        List<string> invalidReferences = [];
        foreach (string sourceFile in sourceFiles)
        {
            string source = File.ReadAllText(sourceFile);
            invalidReferences.AddRange(
                LegacyRasterReference().Matches(source)
                    .Select(match => $"{Path.GetRelativePath(root, sourceFile)}: {match.Value}"));
        }

        Assert.True(
            invalidReferences.Count == 0,
            "Public runtime sources still reference non-WebP raster images:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, invalidReferences));
    }

    [Fact]
    public void PublicWebpReferences_ResolveToCommittedAssets()
    {
        string root = FindRepositoryRoot();
        string webRoot = Path.Combine(root, "Legacy.Maliev.Web");
        string[] sourceFiles = Directory
            .EnumerateFiles(webRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                Path.GetExtension(path) is ".razor" or ".cshtml" or ".cs" or ".css" or ".js" or ".mjs" or ".json")
            .ToArray();

        string[] missing = sourceFiles
            .SelectMany(path => WebpReference().Matches(File.ReadAllText(path)).Select(match => match.Groups["path"].Value))
            .Select(path => path.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !File.Exists(Path.Combine(webRoot, "wwwroot", path)))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0, "Missing referenced WebP assets: " + string.Join(", ", missing));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    [GeneratedRegex("""(?i)(?:data:image/(?:png|jpe?g|gif|bmp|tiff?|avif|x-icon)|(?:~?/|url\(['"]?/?)(?:src/images/[^'"\)\s?#]+|favicon(?:-\d+x\d+)?|apple-touch-icon)\.(?:png|jpe?g|gif|bmp|tiff?|avif|ico))""")]
    private static partial Regex LegacyRasterReference();

    [GeneratedRegex("""(?i)(?<path>~?/(?:src/images/[^'"\)\s?#]+|favicon(?:-\d+x\d+)?|apple-touch-icon)\.webp)""")]
    private static partial Regex WebpReference();
}
