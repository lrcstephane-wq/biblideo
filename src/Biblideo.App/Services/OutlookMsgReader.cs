using System.IO;
using System.Runtime.InteropServices;
using Biblideo.Core.Models;
using Biblideo.Core.Services;

namespace Biblideo.App.Services;

public sealed class OutlookMsgReader
{
    public Task<IReadOnlyList<ParsedLicenseMessage>> ReadManyAsync(IReadOnlyList<string> paths)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<ParsedLicenseMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var results = new List<ParsedLicenseMessage>(paths.Count);
            object? outlook = null;
            object? session = null;
            try
            {
                var outlookType = Type.GetTypeFromProgID("Outlook.Application")
                                  ?? throw new InvalidOperationException("Outlook classique n’est pas installé ou n’est pas accessible.");
                outlook = Activator.CreateInstance(outlookType)
                          ?? throw new InvalidOperationException("Impossible de démarrer Outlook classique.");
                session = outlookType.InvokeMember("Session", System.Reflection.BindingFlags.GetProperty, null, outlook, null);
                if (session is null) throw new InvalidOperationException("La session Outlook n’est pas accessible.");

                foreach (var path in paths)
                    results.Add(ReadOne(session, path));
                completion.TrySetResult(results);
            }
            catch (Exception exception)
            {
                foreach (var path in paths.Skip(results.Count))
                    results.Add(Unreadable(path, exception.GetBaseException().Message));
                completion.TrySetResult(results);
            }
            finally
            {
                ReleaseCom(session);
                ReleaseCom(outlook);
            }
        })
        {
            IsBackground = true,
            Name = "Biblideo-OutlookMsgReader"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static ParsedLicenseMessage ReadOne(object session, string path)
    {
        object? item = null;
        object? recipients = null;
        try
        {
            item = session.GetType().InvokeMember("OpenSharedItem", System.Reflection.BindingFlags.InvokeMethod, null, session, [path]);
            if (item is null) return Unreadable(path, "Outlook n’a pas reconnu ce fichier .msg.");
            var itemType = item.GetType();
            var subject = itemType.InvokeMember("Subject", System.Reflection.BindingFlags.GetProperty, null, item, null)?.ToString() ?? string.Empty;
            var body = itemType.InvokeMember("Body", System.Reflection.BindingFlags.GetProperty, null, item, null)?.ToString() ?? string.Empty;
            recipients = itemType.InvokeMember("Recipients", System.Reflection.BindingFlags.GetProperty, null, item, null);
            var addresses = ReadRecipients(recipients);
            return new ParsedLicenseMessage
            {
                FullPath = path,
                FileName = Path.GetFileName(path),
                Subject = subject,
                Recipients = addresses,
                ExpiryDate = LicenseManagementService.ParseExpiryDate(body)
            };
        }
        catch (Exception exception)
        {
            return Unreadable(path, exception.GetBaseException().Message);
        }
        finally
        {
            ReleaseCom(recipients);
            ReleaseCom(item);
        }
    }

    private static IReadOnlyList<string> ReadRecipients(object? recipients)
    {
        if (recipients is null) return [];
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var type = recipients.GetType();
        var count = Convert.ToInt32(type.InvokeMember("Count", System.Reflection.BindingFlags.GetProperty, null, recipients, null));
        for (var index = 1; index <= count; index++)
        {
            object? recipient = null;
            object? accessor = null;
            try
            {
                recipient = type.InvokeMember("Item", System.Reflection.BindingFlags.InvokeMethod, null, recipients, [index]);
                if (recipient is null) continue;
                var recipientType = recipient.GetType();
                var address = recipientType.InvokeMember("Address", System.Reflection.BindingFlags.GetProperty, null, recipient, null)?.ToString();
                try
                {
                    accessor = recipientType.InvokeMember("PropertyAccessor", System.Reflection.BindingFlags.GetProperty, null, recipient, null);
                    var smtp = accessor?.GetType().InvokeMember(
                        "GetProperty",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        accessor,
                        ["http://schemas.microsoft.com/mapi/proptag/0x39FE001E"])?.ToString();
                    if (!string.IsNullOrWhiteSpace(smtp)) address = smtp;
                }
                catch { /* L’adresse Outlook classique reste utilisée en repli. */ }
                var normalized = LicenseManagementService.NormalizeEmail(address);
                if (normalized.Contains('@')) values.Add(normalized);
            }
            finally
            {
                ReleaseCom(accessor);
                ReleaseCom(recipient);
            }
        }
        return values.ToArray();
    }

    private static ParsedLicenseMessage Unreadable(string path, string error) => new()
    {
        FullPath = path,
        FileName = Path.GetFileName(path),
        Error = error
    };

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }
}
