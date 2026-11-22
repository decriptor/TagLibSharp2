#!/usr/bin/env bash
# Copyright (c) 2025-2026 Stephen Shaw and contributors
# Licensed under the MIT License. See LICENSE file in the project root for full license information.
#
# Mutation-tests the library files a branch changes, with Stryker.NET.
#
#   tools/mutation.sh [base]          # base defaults to origin/main
#
# Changed files come from `git diff` against the merge base, so this works in a git worktree and
# with any revision (`origin/main~3`, a SHA). Stryker's own --since reads the repository through
# LibGit2Sharp, which finds no changes in a worktree and accepts only plain branch names.
#
# Stryker's VSTest runner cannot start the test host under the .NET 11 SDK, so when the default
# SDK is 11 or later and a 10.0 SDK is installed, the run uses a temporary global.json that selects
# it. The file is removed on exit. Reports go to tests/TagLibSharp2.Tests/StrykerOutput/.

set -euo pipefail

base=${1:-origin/main}
root=$(git rev-parse --show-toplevel)
tests="$root/tests/TagLibSharp2.Tests"
merge_base=$(git -C "$root" merge-base "$base" HEAD)

changed=()
while IFS= read -r file; do
	[[ -f "$root/$file" ]] && changed+=("$file")
done < <(git -C "$root" diff --name-only "$merge_base" HEAD -- 'src/TagLibSharp2/*.cs')
# Uncommitted edits and new files count too.
while IFS= read -r file; do
	[[ -f "$root/$file" && ! " ${changed[*]-} " =~ " $file " ]] && changed+=("$file")
done < <(git -C "$root" diff --name-only HEAD -- 'src/TagLibSharp2/*.cs'; git -C "$root" ls-files --others --exclude-standard -- 'src/TagLibSharp2/*.cs')

if [[ ${#changed[@]} -eq 0 ]]; then
	echo "No library files changed since $base ($merge_base)."
	exit 0
fi

echo "Mutating ${#changed[@]} file(s) changed since $base:"
printf '  %s\n' "${changed[@]}"

args=()
for file in "${changed[@]}"; do
	args+=(--mutate "**/${file#src/TagLibSharp2/}")
done

# Stryker injects a public MutantControl type, so any PublicApi snapshot test that still runs writes a
# .received.txt beside its snapshot. stryker-config.json filters those tests out; remove any file a run
# leaves anyway, but never one that was there before.
snapshots="$tests/PublicApi/Snapshots"
received_before=$(ls "$snapshots"/*.received.* 2> /dev/null || true)
cleanup=()
remove_new_received () {
	for f in "$snapshots"/*.received.*; do
		[[ -e "$f" ]] || continue
		grep -qxF "$f" <<< "$received_before" || rm -f "$f"
	done
}
trap 'for f in "${cleanup[@]-}"; do [[ -n "$f" ]] && rm -f "$f"; done; remove_new_received' EXIT
major=$(cd "$tests" && dotnet --version | cut -d. -f1)
if [[ "$major" -ge 11 && ! -e "$tests/global.json" ]]; then
	sdk10=$(dotnet --list-sdks | awk '/^10\./ { print $1 }' | sort -V | tail -1)
	if [[ -n "$sdk10" ]]; then
		echo "{ \"sdk\": { \"version\": \"$sdk10\", \"rollForward\": \"disable\" } }" > "$tests/global.json"
		cleanup+=("$tests/global.json")
		echo "Using SDK $sdk10 for Stryker (the default SDK is $major.x)."
	else
		echo "warning: no 10.0 SDK installed; Stryker's VSTest runner may fail to start the test host." >&2
	fi
fi

cd "$tests"
dotnet tool restore > /dev/null
dotnet stryker "${args[@]}"
