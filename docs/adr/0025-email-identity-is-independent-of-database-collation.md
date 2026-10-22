# Email identity is independent of database collation and never selects a username

The vault trims and compares email identities case-insensitively, preserving the entered spelling
for display and delivery. One application normalization policy and matching database uniqueness
apply in SQL Server and PostgreSQL. Provider-specific dot or plus-address folding is not part of
this policy. Leaving identity selection to deployment collation would make the same address select
different accounts or fail to match after moving database providers.

Migration checks every existing account, including disabled accounts. Canonical collisions and
cross-account alias collisions require explicit operator resolution before migration; accounts are
not merged or assigned an owner automatically. Activation rechecks uniqueness, and the database
constraint protects the race. A refused address gives a generic conflict without identifying the
other account.

SSO uses an email-only lookup. Callers accepting either a username or email refuse matches to
different account ids rather than choosing the first row. Core creation enforces the same username
rules as the Web form, keeping new username and email namespaces disjoint. Username case policy and
the OIDC protocol are unchanged. A persisted email-shaped username is possible through custom
provisioning because the former Core checks were weaker than the Web checks; the ordinary
administrator form already rejects it.
