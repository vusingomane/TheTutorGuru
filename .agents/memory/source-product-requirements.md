---
name: Source product requirements
description: Standing product rules supplied in the original TheTutorGuru build brief.
---

The supplied brief specifies Mathematics for grades 8–12, Maths Literacy and Physical Science for grades 10–12, and Afrikaans for grades 11–12.

Subscriptions cost R290/month for one subject or R495/month for two subjects. Classes are two live sessions per week, one hour each, with 30–180 learners and voice or text questions. The stated policy is a full refund within 30 days.

The requested architecture is .NET Minimal API, MudBlazor, Dapper, ASP.NET Core Identity, and PayGate or Peach Payments. A React/Express preview is not an approved replacement for that architecture.

The user explicitly chose the original .NET architecture after being told that replacing Clerk would require new sign-ins, potentially new passwords, and verified migration of account ownership. Preserve legacy profile identifiers and ownership references during account migration; never claim an existing profile solely because an email matches.

**Why:** The user supplied these requirements in the original build brief and explicitly reiterated: "please use the .NET/MudBlazor architecture." The user confirmed the architecture and migration consequences. Retaining ownership identifiers avoids breaking parent–learner, tutor, enrolment, and uploaded-file relationships.

**How to apply:** Preserve these values and eligibility rules in future implementation. Keep the distinction between a working preview and the requested complete architecture explicit; seek agreement before replacing the requested stack or payment provider.
