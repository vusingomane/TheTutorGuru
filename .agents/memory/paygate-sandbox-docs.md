---
name: PayGate sandbox documentation
description: Conflicting official sandbox examples and how to establish the usable testing preset.
---

PayGate's general sandbox page and product-specific testing pages disagree about the public test
account settings. The public PayWeb3 and PayHost account use different product presets.
Use the product-specific testing reference and independently verify the request with the gateway.

**Why:** During integration testing, one published preset produced a PayWeb checksum error and a
PayHost authentication rejection. PayHost can return HTTP 200 with a SOAP authentication error;
inspect the safe result description instead of inferring authentication from HTTP success.
PayHost follow-up statuses are not PayWeb authorisation statuses: interpret refunds by their
transaction type and confirmed provider description, not a shared numeric success/decline table.
Public terminal capabilities can also differ from the documentation's 3-D Secure labels; verify
merchant-initiated recurring capability independently of a successful hosted checkout.
A working public sandbox is not proof that the
user's merchant account has recurring processing and refunds enabled.

**How to apply:** Confirm signed PayWeb replies and authenticated PayHost outcomes before diagnosing
an application bug or asking for merchant secrets. Use the live WSDL for SOAP shape. Never promote
the public test account to production or use it as a fallback for broken merchant credentials.
