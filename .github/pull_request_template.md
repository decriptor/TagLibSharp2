## Description

<!-- Describe your changes in detail -->

## Motivation and Context

<!-- Why is this change required? What problem does it solve? -->
<!-- If it fixes an open issue, please link to the issue here -->

Fixes #

## Type of Change

<!-- Put an x in the boxes that apply -->

- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Performance improvement
- [ ] Code refactoring (no functional changes)

## Checklist

<!-- Put an x in the boxes that apply -->

- [ ] I have read the [CONTRIBUTING](../CONTRIBUTING.md) guidelines
- [ ] My code follows the project's code style
- [ ] I have added tests that prove my fix/feature works
- [ ] All new and existing tests pass (`dotnet test`)
- [ ] I have updated documentation as needed
- [ ] My changes generate no new warnings

### Implementation guide ([docs/IMPLEMENTATION-GUIDE.md](https://github.com/decriptor/TagLibSharp2/blob/main/docs/IMPLEMENTATION-GUIDE.md))

- [ ] Design note with the behaviour table: writers, what each reader shows before and after, the spec citation per cell
- [ ] Twin table: every entry point, sibling format, write path, view, and render/parse, each with its pinning test
- [ ] Kit run on the final head: before/after grid, fuzz, depth tests, allocation bounds, reader comparison, mutation testing (results below)
- [ ] Pre-review: an independent review of the diff at the reviewer checklist level, findings fixed or declined with evidence

## Testing

<!-- Describe how you tested your changes -->

## Additional Notes

<!-- Any additional information that reviewers should know -->
