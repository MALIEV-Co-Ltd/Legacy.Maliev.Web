using System.Net.Mail;

namespace Legacy.Maliev.Web.Application;

internal sealed class CustomerOrderSubmissionService(
    ICustomerOrderSubmissionTransport transport,
    INotificationClient notificationClient) : ICustomerOrderSubmissionService
{
    public async Task<CustomerOrderSubmissionResult> SubmitAsync(
        int trustedCustomerId,
        string trustedCustomerEmail,
        CustomerOrderDraft draft,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (!IsValid(trustedCustomerId, trustedCustomerEmail, draft, operationId))
        {
            return Rejected();
        }

        var operationPrefix = $"legacy-web-member-order-{operationId:D}";
        var created = await transport.CreateAsync(
            trustedCustomerId,
            draft,
            operationPrefix,
            cancellationToken);
        if (created.OrderId is not > 0)
        {
            return new(
                null,
                false,
                false,
                created.ServiceAvailable,
                created.Authorized,
                created.Conflict,
                false,
                false);
        }

        var orderId = created.OrderId.Value;
        var status = await transport.AddNewStatusAsync(
            orderId,
            $"{operationPrefix}-status-new",
            cancellationToken);
        if (!status.Succeeded)
        {
            return Partial(orderId, status, filesUploaded: false);
        }

        var filesUploaded = false;
        if (draft.Files.Count > 0)
        {
            var upload = await transport.UploadAsync(
                trustedCustomerId,
                draft.Files,
                $"{operationPrefix}-upload",
                cancellationToken);
            if (upload.Objects is null)
            {
                return new(
                    orderId,
                    false,
                    true,
                    upload.ServiceAvailable,
                    upload.Authorized,
                    upload.Conflict,
                    false,
                    false);
            }

            foreach (var uploadedObject in upload.Objects)
            {
                var link = await transport.LinkAsync(
                    trustedCustomerId,
                    orderId,
                    uploadedObject,
                    cancellationToken);
                if (!link.Succeeded)
                {
                    return Partial(orderId, link, filesUploaded: false);
                }
            }

            filesUploaded = true;
        }

        var notification = await notificationClient.SendAsync(
            NotificationChannel.NoReply,
            new EmailNotification(
                trustedCustomerEmail.Trim(),
                $"{OrderTypeName(draft.Kind)} Order #{orderId}",
                ConfirmationBody,
                null,
                null,
                ["manufacturing@maliev.com"]),
            cancellationToken);

        return new(
            orderId,
            notification.Sent,
            true,
            notification.ServiceAvailable,
            notification.Authorized,
            false,
            filesUploaded,
            notification.Sent);
    }

    private const string ConfirmationBody = """
        <div>Thank you for placing your request.</div>
        <div>&nbsp;</div>
        <div>We will review your order and get back to you with a detailed quotation as soon as possible.</div>
        <div>In the meanwhile, our engineer may contact you for further information.</div>
        <div>&nbsp;</div>
        <div>Best regards,</div>
        <div>Maliev Co., Ltd.</div>
        """;

    private static CustomerOrderSubmissionResult Partial(
        int orderId,
        CustomerOrderOperationResult operation,
        bool filesUploaded) => new(
            orderId,
            false,
            true,
            operation.ServiceAvailable,
            operation.Authorized,
            operation.Conflict,
            filesUploaded,
            false);

    private static CustomerOrderSubmissionResult Rejected() => new(
        null,
        false,
        false,
        true,
        false,
        false,
        false,
        false);

    private static bool IsValid(
        int trustedCustomerId,
        string trustedCustomerEmail,
        CustomerOrderDraft draft,
        Guid operationId)
    {
        if (trustedCustomerId <= 0
            || operationId == Guid.Empty
            || !Enum.IsDefined(draft.Kind)
            || string.IsNullOrWhiteSpace(draft.Name)
            || draft.Name.Length > 200
            || draft.ProcessId <= 0
            || draft.Quantity <= 0
            || draft.Files is null
            || draft.Files.Any(file => file is null
                || file.Length <= 0
                || string.IsNullOrWhiteSpace(file.FileName)))
        {
            return false;
        }

        if (draft.Kind == CustomerOrderKind.Additive
            && (draft.MaterialId is not > 0
                || draft.SurfaceFinishId is not > 0
                || draft.ColorId is not > 0))
        {
            return false;
        }

        if (draft.Kind == CustomerOrderKind.Machining
            && (draft.MaterialId is not > 0 || draft.SurfaceFinishId is not > 0))
        {
            return false;
        }

        try
        {
            var address = new MailAddress(trustedCustomerEmail.Trim());
            return string.Equals(address.Address, trustedCustomerEmail.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string OrderTypeName(CustomerOrderKind kind) => kind switch
    {
        CustomerOrderKind.Additive => "3D Printing",
        CustomerOrderKind.Scanning => "3D Scanning",
        CustomerOrderKind.Machining => "CNC Machining",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
