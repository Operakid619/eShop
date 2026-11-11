# Checkout payment-data protection

This contribution to Emmanuel's eShop fork addresses two concrete checkout issues:

1. Missing request IDs previously caused the complete payment request to be sent
   to structured logging. Command, validation, and transaction pipeline logs could
   also attach nested card data. These paths now log metadata rather than payloads;
   validation logs retain field names without attempted values.
2. Null, blank, and shorter-than-four-character card numbers could reach substring
   masking and throw. They now return HTTP 400 before command dispatch. Valid-length
   inputs retain the existing last-four-character masking behavior.

Request and transaction IDs remain available for tracing. This is not card-number
verification, a payment gateway integration, or a payment-compliance certification.
Exception messages and custom logs elsewhere still need their own review.

The regression tests inspect both rendered strings and structured logging values,
including nested commands and rejected validation values.

```sh
dotnet test --project tests/Ordering.UnitTests/Ordering.UnitTests.csproj
```

The upstream project, license, and commit history are retained.
