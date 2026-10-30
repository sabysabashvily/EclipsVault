# Verify mailbox ownership before activating a replacement email address

Email is a vault sign-in alias and the SSO account-matching key, not just a contact detail. A change
therefore needs fresh account verification and proof of ownership of the proposed mailbox before
that address becomes active. The old address stays active while confirmation is pending or delivery
fails, and receives a notification about the change.

One pending target per account receives a single-use mailbox code valid for fifteen minutes, entered
on the original signed-in Profile page. The five-minute account verification must still be valid at
activation; otherwise it is repeated for that exact address. The operation authorization is
consumed at activation, not spent on requesting the email. Replacement requests and resends invalidate
the previous code, and the person can cancel the pending change.

Five incorrect mailbox-code attempts invalidate that code. Delivery attempts are limited to one per
minute and six per hour per account, with source-IP protection and shared enforcement across nodes.
Mailbox-code mistakes do not lock ordinary sign-in. Delivery failure leaves the active email
unchanged. Codes are entered on Profile rather than carried by state-changing email links.

Self-service email changes are unavailable, with an explanation, when real mail delivery is not
configured and enabled. The Log transport and a recorded send cannot prove mailbox ownership.
Immediate replacement after account verification alone was rejected because it lets the account
holder bind sign-in to an address they do not control.

Confirmation secrets must not appear in application logs, audit details or stored notification
bodies. The existing notification history stores message bodies, so verification delivery cannot
reuse that recording behavior unchanged. Completing the active change follows the credential
generation and session rules in ADR-0023.

Email identity comparison and uniqueness follow ADR-0025, including a generic conflict when another
account takes the address before activation.
