# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

Note: this is a Windows desktop application using the native Windows design language. It is not iOS/Android, so the schema's non-mobile value is recorded; the exact UI framework is undecided.

## Users
Windows developers who already know Docker Desktop and want an equivalent graphical way to manage WSLc (WSL containers): running containers, images, and related workflows without living in the CLI.

## Product Purpose
A desktop app for managing WSLc for users familiar with "that other product" (Docker Desktop). Success: a Docker Desktop user can find and do their usual container tasks in WSLc without relearning concepts.

## Positioning
Built natively around WSLc rather than a VM- and daemon-based stack, so it can be lighter and tied to WSL. Familiar to Docker Desktop users, but not a clone. (Inferred from the interview answer; exact performance claims are unverified.)

## Operating Context
Used on Windows alongside WSL and developer tooling. Users arrive with Docker Desktop mental models (containers, images, volumes, logs).

## Capabilities and Constraints
- Platform: Windows desktop, native design language.
- Feature scope, tech stack, and WSLc integration surface are undecided; the repo currently contains only a README, LICENSE, and .gitignore.
- Terminology should map clearly to what Docker Desktop users already know.

## Brand Commitments
None established. The name is "wslc-desktop".

## Evidence on Hand
None. There is no existing code, copy, screenshots, or testimonials. Do not fabricate benchmarks or customer claims.

## Product Principles
- Familiar to Docker Desktop users first: reuse their vocabulary and workflows.
- Be honest about WSLc's behavior; do not imitate Docker features WSLc lacks.
- Feel native on Windows.
- Make common container tasks fast and low-friction.

## Accessibility & Inclusion
No product-specific requirement stated; follow Windows platform accessibility conventions.
