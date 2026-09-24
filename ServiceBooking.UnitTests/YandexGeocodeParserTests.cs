using FluentAssertions;
using ServiceBooking.API.Services.Geo;
using ServiceBooking.API.Services.Geo.Yandex;
using ServiceBooking.Core.Enums;

namespace ServiceBooking.UnitTests;

/// <summary>ARCHITECTURE_CYCLE13.md §207/§214 — pure JSON parsing on recorded fixtures, no network. Each
/// fixture is a minimal but realistic shape of the Yandex Geocoder's own documented response.</summary>
public class YandexGeocodeParserTests
{
    private const string HouseFixture = """
        {
          "response": {
            "GeoObjectCollection": {
              "featureMember": [
                {
                  "GeoObject": {
                    "metaDataProperty": {
                      "GeocoderMetaData": {
                        "precision": "exact",
                        "kind": "house",
                        "text": "Россия, Алтайский край, Барнаул, проспект Ленина, 5",
                        "Address": {
                          "formatted": "Россия, Алтайский край, Барнаул, проспект Ленина, 5",
                          "Components": [
                            { "kind": "country", "name": "Россия" },
                            { "kind": "locality", "name": "Барнаул" },
                            { "kind": "street", "name": "проспект Ленина" },
                            { "kind": "house", "name": "5" }
                          ]
                        }
                      }
                    },
                    "Point": { "pos": "83.767436 53.346383" }
                  }
                }
              ]
            }
          }
        }
        """;

    private const string StreetFixture = """
        {
          "response": {
            "GeoObjectCollection": {
              "featureMember": [
                {
                  "GeoObject": {
                    "metaDataProperty": {
                      "GeocoderMetaData": {
                        "precision": "street",
                        "kind": "street",
                        "Address": {
                          "formatted": "Россия, Алтайский край, Барнаул, проспект Ленина",
                          "Components": [
                            { "kind": "locality", "name": "Барнаул" },
                            { "kind": "street", "name": "проспект Ленина" }
                          ]
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
        }
        """;

    private const string LocalityFixture = """
        {
          "response": {
            "GeoObjectCollection": {
              "featureMember": [
                {
                  "GeoObject": {
                    "metaDataProperty": {
                      "GeocoderMetaData": {
                        "precision": "other",
                        "kind": "locality",
                        "Address": {
                          "formatted": "Россия, Алтайский край, Барнаул",
                          "Components": [
                            { "kind": "locality", "name": "Барнаул" }
                          ]
                        }
                      }
                    }
                  }
                }
              ]
            }
          }
        }
        """;

    private const string EmptyFixture = """
        {
          "response": {
            "GeoObjectCollection": {
              "featureMember": []
            }
          }
        }
        """;

    private const string UnknownPrecisionFixture = """
        {
          "response": {
            "GeoObjectCollection": {
              "featureMember": [
                {
                  "GeoObject": {
                    "metaDataProperty": {
                      "GeocoderMetaData": {
                        "precision": "some-future-value",
                        "Address": { "formatted": "Some address" }
                      }
                    }
                  }
                }
              ]
            }
          }
        }
        """;

    [Fact]
    public void Parse_House_ReturnsHousePrecisionAndPoint()
    {
        var candidates = YandexGeocodeParser.Parse(HouseFixture);

        candidates.Should().NotBeNull();
        candidates!.Should().HaveCount(1);
        candidates![0].Precision.Should().Be(AddressPrecision.House);
        candidates[0].FormattedAddress.Should().Be("Россия, Алтайский край, Барнаул, проспект Ленина, 5");
        candidates[0].CityName.Should().Be("Барнаул");
        candidates[0].Point.Should().NotBeNull();
    }

    [Fact]
    public void Parse_Point_LongitudeFirst_NotLatitude()
    {
        // Point.pos is "lon lat" — a classic mix-up; asymmetric numbers so a swap fails loudly.
        var candidates = YandexGeocodeParser.Parse(HouseFixture);

        candidates![0].Point!.Value.Longitude.Should().BeApproximately(83.767436, 0.0001);
        candidates[0].Point!.Value.Latitude.Should().BeApproximately(53.346383, 0.0001);
    }

    [Fact]
    public void Parse_Street_ReturnsStreetPrecision_NoPoint()
    {
        var candidates = YandexGeocodeParser.Parse(StreetFixture);

        candidates.Should().NotBeNull();
        candidates![0].Precision.Should().Be(AddressPrecision.Street);
        candidates[0].Point.Should().BeNull();
    }

    [Fact]
    public void Parse_LocalityOnly_ReturnsLocalityPrecision()
    {
        var candidates = YandexGeocodeParser.Parse(LocalityFixture);

        candidates.Should().NotBeNull();
        candidates![0].Precision.Should().Be(AddressPrecision.Locality);
    }

    [Fact]
    public void Parse_EmptyFeatureMember_ReturnsEmptyNonNullList()
    {
        var candidates = YandexGeocodeParser.Parse(EmptyFixture);

        candidates.Should().NotBeNull();
        candidates.Should().BeEmpty();
    }

    [Fact]
    public void Parse_UnrecognizedPrecisionValue_FallsBackToOther()
    {
        var candidates = YandexGeocodeParser.Parse(UnknownPrecisionFixture);

        candidates.Should().NotBeNull();
        candidates![0].Precision.Should().Be(AddressPrecision.Other);
    }

    [Fact]
    public void Parse_TruncatedJson_ReturnsNull()
    {
        var candidates = YandexGeocodeParser.Parse("""{ "response": { "GeoObjectColl""");
        candidates.Should().BeNull();
    }

    [Fact]
    public void Parse_MissingResponseKey_ReturnsNull()
    {
        var candidates = YandexGeocodeParser.Parse("""{ "notResponse": {} }""");
        candidates.Should().BeNull();
    }

    [Fact]
    public void Parse_NotJsonAtAll_ReturnsNull()
    {
        var candidates = YandexGeocodeParser.Parse("this is not json");
        candidates.Should().BeNull();
    }

    [Fact]
    public void Parse_MissingFormattedAddress_FallsBackToTopLevelText()
    {
        const string fixture = """
            {
              "response": {
                "GeoObjectCollection": {
                  "featureMember": [
                    {
                      "GeoObject": {
                        "metaDataProperty": {
                          "GeocoderMetaData": {
                            "precision": "exact",
                            "text": "Fallback text address"
                          }
                        }
                      }
                    }
                  ]
                }
              }
            }
            """;

        var candidates = YandexGeocodeParser.Parse(fixture);

        candidates.Should().NotBeNull();
        candidates![0].FormattedAddress.Should().Be("Fallback text address");
    }
}
