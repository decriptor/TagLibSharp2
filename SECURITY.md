# Security Policy

## Supported Versions

| Version | Supported          |
| ------- | ------------------ |
| 1.x     | :white_check_mark: |
| < 1.0   | :x: (pre-release)  |

## Reporting a Vulnerability

If you discover a security vulnerability in TagLibSharp2, please report it responsibly:

1. **Do NOT** open a public GitHub issue for security vulnerabilities
2. Use GitHub's private vulnerability reporting: https://github.com/decriptor/tagsharp/security/advisories/new
3. Include:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if any)

We will acknowledge receipt within 48 hours and provide a detailed response within 7 days.

## Security Considerations for Users

### Input Validation

TagLibSharp2 parses untrusted binary data from media files. While we implement defensive parsing:

- **Malformed files**: The library uses result types rather than exceptions to handle malformed data gracefully
- **Large files**: Consider memory limits when loading entire files; the library does not currently enforce size limits
- **Embedded content**: Album art and other embedded data are passed through without sanitization. Applications should validate image data before rendering

### Memory Safety

- All parsing uses `Span<T>` and bounds checking
- `ArrayPool<byte>` is used for temporary allocations
- `BinaryData` is immutable after construction

### Dependency Security

TagLibSharp2 has no external runtime dependencies, reducing supply chain risk.

## Security Updates

Security fixes will be released as patch versions (e.g., 1.0.1) and announced via:
- GitHub Security Advisories
- Release notes
