using System.Xml.Linq;

namespace Legacy.Maliev.Web.Tests;

public sealed class ThaiResourceParityTests
{
    [Fact]
    public void SourceBackedFormsAndSharedNavigationHaveThaiValuesForEveryMigratedKey()
    {
        var expected = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["Resources/Components/Pages/Contact/ContactContent.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Chat channels"] = "ช่องทางแชต",
                ["Message us on WhatsApp"] = "ส่งข้อความผ่าน WhatsApp",
                ["Open Messenger"] = "เปิด Messenger",
                ["Email address is required"] = "กรุณากรอกอีเมล",
                ["Please enter your first name"] = "กรุณากรอกชื่อ",
                ["Please enter your last name"] = "กรุณากรอกนามสกุล",
                ["Please enter your message"] = "กรุณากรอกข้อความ",
                ["Select a country"] = "เลือกประเทศ",
                ["Telephone"] = "โทรศัพท์",
                ["The map is temporarily unavailable."] = "ขณะนี้แผนที่ไม่พร้อมใช้งานชั่วคราว"
            },
            ["Resources/Components/Pages/Account/LoginContent.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Email address is required"] = "กรุณากรอกอีเมล",
                ["Password is required"] = "กรุณากรอกรหัสผ่าน",
                ["Resend verification email"] = "ส่งอีเมลยืนยันใหม่",
                ["Email verification required"] = "ต้องยืนยันอีเมล",
                ["Please verify your email before signing in."] = "โปรดยืนยันอีเมลก่อนเข้าสู่ระบบ",
                ["If the account requires verification, a new verification link has been sent."] = "หากบัญชีต้องยืนยันอีเมล ระบบได้ส่งลิงก์ยืนยันใหม่แล้ว",
                ["We could not send the verification email. Please try again later."] = "ไม่สามารถส่งอีเมลยืนยันได้ โปรดลองอีกครั้งภายหลัง",
                ["Login failed"] = "เข้าสู่ระบบไม่สำเร็จ",
                ["Too many failed attempts. This account has been locked out, please try again later."] = "มีการพยายามเข้าสู่ระบบไม่สำเร็จหลายครั้ง บัญชีถูกล็อกชั่วคราว โปรดลองอีกครั้งภายหลัง",
                ["Log in required Two-Factor Authentication"] = "การเข้าสู่ระบบต้องยืนยันตัวตนแบบสองขั้นตอน",
                ["Please verify your input again."] = "โปรดตรวจสอบข้อมูลที่กรอกอีกครั้ง",
                ["Hello,"] = "สวัสดี",
                ["Email Confirmation"] = "ยืนยันอีเมล",
                ["Please confirm your MALIEV customer email address by selecting the link below."] = "โปรดยืนยันอีเมลสำหรับบัญชีลูกค้า MALIEV โดยเลือกลิงก์ด้านล่าง",
                ["Confirm email"] = "ยืนยันอีเมล",
                ["If you did not request this email, you can ignore it."] = "หากคุณไม่ได้ขออีเมลนี้ สามารถละเว้นข้อความนี้ได้",
                ["New to MALIEV?"] = "ยังไม่มีบัญชีกับ MALIEV?"
            },
            ["Resources/Components/Pages/Account/ForgotPasswordContent.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Email address is required"] = "กรุณากรอกอีเมล",
                ["Must be a valid email address"] = "กรุณากรอกอีเมลที่ถูกต้อง"
            },
            ["Resources/Components/Pages/Account/ResetPasswordContent.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Please confirm your new password"] = "กรุณายืนยันรหัสผ่านใหม่",
                ["Email address is required"] = "กรุณากรอกอีเมล",
                ["Please enter a new password"] = "กรุณากรอกรหัสผ่านใหม่"
            },
            ["Resources/Components/Pages/Account/SignupContent.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Please retype your password"] = "กรุณากรอกรหัสผ่านอีกครั้ง",
                ["Please enter an email address"] = "กรุณากรอกอีเมล",
                ["Please enter a valid email address"] = "กรุณากรอกอีเมลที่ถูกต้อง",
                ["First name is required"] = "กรุณากรอกชื่อ",
                ["Last name is required"] = "กรุณากรอกนามสกุล",
                ["Password is required"] = "กรุณากรอกรหัสผ่าน",
                ["Password does not match"] = "รหัสผ่านไม่ตรงกัน",
                ["We could not create your account. Please try again, or contact us if the problem continues."] = "ระบบไม่สามารถสร้างบัญชีของคุณได้ กรุณาลองใหม่อีกครั้ง หรือติดต่อเราหากยังพบปัญหา",
                ["We could not clean up the failed sign-up. Please contact us before trying again."] = "ระบบไม่สามารถล้างข้อมูลการสมัครที่ไม่สำเร็จได้ กรุณาติดต่อเราก่อนสมัครใหม่",
                ["We could not send the confirmation email. Please try again later."] = "ระบบไม่สามารถส่งอีเมลยืนยันได้ กรุณาลองใหม่ภายหลัง",
                ["We could not finish setting up your account. Please contact us."] = "ระบบตั้งค่าบัญชีของคุณไม่สำเร็จ กรุณาติดต่อเรา",
                ["Account created. Check your email and follow the link to confirm your address before signing in."] = "สร้างบัญชีเรียบร้อยแล้ว กรุณาตรวจสอบอีเมลและกดลิงก์ยืนยันก่อนเข้าสู่ระบบ"
            },
            ["Resources/Components/Pages/Quotation/QuotationContent.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Email address is required"] = "กรุณากรอกอีเมล",
                ["Please enter your first name"] = "กรุณากรอกชื่อ",
                ["Please enter your last name"] = "กรุณากรอกนามสกุล",
                ["Please enter your message"] = "กรุณากรอกข้อความ"
            },
            ["Resources/Components/Layout/PublicNavigation.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Finishing and colour standards"] = "การเก็บผิวและมาตรฐานสี"
            },
            ["Resources/Components/Layout/PublicFooter.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["3D design"] = "ออกแบบ 3 มิติ",
                ["Custom manufacturing"] = "ผลิตชิ้นงานตามแบบ",
                ["Low-volume injection molding"] = "ฉีดพลาสติกจำนวนน้อย",
                ["Silicone casting"] = "หล่อซิลิโคน",
                ["All services"] = "บริการทั้งหมด"
            },
            ["Resources/Components/Pages/Account/AccountIndexContent.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Signed in as"] = "เข้าสู่ระบบในชื่อ",
                ["Manage profile"] = "จัดการโปรไฟล์",
                ["Manage addresses"] = "จัดการที่อยู่",
                ["Change email"] = "เปลี่ยนอีเมล",
                ["Change password"] = "เปลี่ยนรหัสผ่าน",
                ["Orders"] = "คำสั่งซื้อ"
            },
            ["Resources/Components/Pages/Account/EmailConfirmationContent.th.resx"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Email Confirmation | MALIEV"] = "ยืนยันอีเมล | MALIEV"
            }
        };

        var root = FindRepositoryRoot();
        foreach (var (relativePath, entries) in expected)
        {
            var path = Path.Combine(root, "Legacy.Maliev.Web", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Expected Thai resource '{path}'.");

            var document = XDocument.Load(path);
            var values = document.Root!
                .Elements("data")
                .Where(element => element.Attribute("name") is not null)
                .ToDictionary(
                    element => (string)element.Attribute("name")!,
                    element => (string?)element.Element("value") ?? string.Empty,
                    StringComparer.Ordinal);

            foreach (var (key, value) in entries)
            {
                Assert.True(values.TryGetValue(key, out var actual), $"Thai resource '{relativePath}' is missing '{key}'.");
                Assert.Equal(value, actual);
                Assert.NotEqual(key, actual);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Legacy.Maliev.Web.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
