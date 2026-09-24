using System.Text.Json;
using FluentAssertions;
using ServiceBooking.API.Services.PhoneVerification.Max;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE14.md §144.3 — pure parsing of the two update shapes the webhook cares
/// about, plus tolerance of unknown/missing fields (§167's "неизвестные поля игнорируются").</summary>
public class MaxUpdateParserTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Parse_BotStarted_ExtractsPayloadAndSenderFromTopLevelUser()
    {
        var root = Parse("""
            { "update_type": "bot_started", "payload": "v1.abc123", "user": { "user_id": 555 } }
            """);

        var result = MaxUpdateParser.Parse(root);

        result.UpdateType.Should().Be("bot_started");
        result.Payload.Should().Be("v1.abc123");
        result.SenderId.Should().Be("555");
        result.Contact.Should().BeNull();
    }

    [Fact]
    public void Parse_MessageWithContactAttachment_ExtractsVcfHashAndOwnerFromMaxInfo()
    {
        var root = Parse("""
            {
              "update_type": "message_created",
              "message": {
                "sender": { "user_id": 777 },
                "body": {
                  "attachments": [
                    {
                      "type": "contact",
                      "payload": {
                        "vcf_info": "BEGIN:VCARD...",
                        "hash": "deadbeef",
                        "max_info": { "user_id": 777 }
                      }
                    }
                  ]
                }
              }
            }
            """);

        var result = MaxUpdateParser.Parse(root);

        result.UpdateType.Should().Be("message_created");
        result.SenderId.Should().Be("777");
        result.Contact.Should().NotBeNull();
        result.Contact!.VcfInfo.Should().Be("BEGIN:VCARD...");
        result.Contact.Hash.Should().Be("deadbeef");
        result.Contact.OwnerId.Should().Be("777");
    }

    [Fact]
    public void Parse_ContactOwnerFallsBackToTamInfo_WhenMaxInfoAbsent()
    {
        var root = Parse("""
            {
              "update_type": "message_created",
              "message": {
                "sender": { "user_id": 1 },
                "body": { "attachments": [ { "type": "contact", "payload": {
                    "vcf_info": "V", "hash": "H", "tam_info": { "user_id": 42 }
                } } ] }
              }
            }
            """);

        MaxUpdateParser.Parse(root).Contact!.OwnerId.Should().Be("42");
    }

    [Fact]
    public void Parse_ContactAttachedFromForeignAccount_OwnerIdDiffersFromSender()
    {
        // The main attack shape (US-14-04): a contact attachment whose OWNER isn't the sender.
        var root = Parse("""
            {
              "update_type": "message_created",
              "message": {
                "sender": { "user_id": 1 },
                "body": { "attachments": [ { "type": "contact", "payload": {
                    "vcf_info": "V", "hash": "H", "max_info": { "user_id": 999 }
                } } ] }
              }
            }
            """);

        var result = MaxUpdateParser.Parse(root);
        result.SenderId.Should().Be("1");
        result.Contact!.OwnerId.Should().Be("999");
        result.SenderId.Should().NotBe(result.Contact.OwnerId);
    }

    [Fact]
    public void Parse_ContactWithNoOwnerInfoAtAll_OwnerIdIsNull()
    {
        var root = Parse("""
            {
              "update_type": "message_created",
              "message": {
                "sender": { "user_id": 1 },
                "body": { "attachments": [ { "type": "contact", "payload": { "vcf_info": "V", "hash": "H" } } ] }
              }
            }
            """);

        MaxUpdateParser.Parse(root).Contact!.OwnerId.Should().BeNull();
    }

    [Fact]
    public void Parse_UnknownUpdateType_DoesNotThrow_JustProducesNoActionableFields()
    {
        var root = Parse("""{ "update_type": "some_future_update", "future_field": 123 }""");
        var result = MaxUpdateParser.Parse(root);
        result.UpdateType.Should().Be("some_future_update");
        result.Contact.Should().BeNull();
    }

    [Fact]
    public void Parse_NonContactAttachment_IsIgnored()
    {
        var root = Parse("""
            {
              "update_type": "message_created",
              "message": {
                "sender": { "user_id": 1 },
                "body": { "attachments": [ { "type": "image", "payload": { "url": "x" } } ] }
              }
            }
            """);

        MaxUpdateParser.Parse(root).Contact.Should().BeNull();
    }

    [Fact]
    public void Parse_MissingFieldsEverywhere_ReturnsAllNullsWithoutThrowing()
    {
        var root = Parse("{}");
        var result = MaxUpdateParser.Parse(root);
        result.UpdateType.Should().BeNull();
        result.Payload.Should().BeNull();
        result.SenderId.Should().BeNull();
        result.Contact.Should().BeNull();
    }
}
