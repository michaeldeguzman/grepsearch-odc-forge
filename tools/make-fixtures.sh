#!/usr/bin/env bash
# Generates synthetic log files (1 MB and 20 MB) and a synthetic Markdown spec for testing.
# Output goes to spike-files/ (gitignored). Contains no real data.
set -euo pipefail
cd "$(dirname "$0")/.."
mkdir -p spike-files

gen() {
  local target_bytes=$1 out=$2
  awk -v target="$target_bytes" 'BEGIN {
    srand(42); bytes = 0; n = 0
    split("INFO INFO INFO INFO WARN DEBUG ERROR", lv, " ")
    while (bytes < target) {
      n++
      level = lv[int(rand()*7)+1]
      s = sprintf("2026-11-02 %02d:%02d:%02d %-5s svc-%02d req=%08d user=u%05d latency=%dms path=/api/v1/orders/%d status=%d",
                  int(n/3600)%24, int(n/60)%60, n%60, level, n%17, n, int(rand()*99999),
                  int(rand()*900)+10, int(rand()*99999), (level=="ERROR") ? 500 : 200)
      if (n % 5000 == 0) s = s " ERR-4012 Payment declined"
      if (n % 12345 == 0) s = s " NEEDLE-UNIQUE-7731"
      print s
      bytes += length(s) + 1
    }
  }' > "$out"
}

gen $((1 * 1024 * 1024))  spike-files/spike-1mb.log
gen $((20 * 1024 * 1024)) spike-files/spike-20mb.log

# Markdown fixture for ReadFileLines and GetFileOutline.
# 1 H1 + 40 H2 + 200 H3 + 600 H4 = 841 real headings. Each H4 section also has a fenced block holding
# "# fake heading" lines, one "##### too deep" line and one "#hashtag" line. None of those are headings.
awk 'BEGIN {
  print "# Synthetic Payment Service Spec"
  print ""
  print "Generated test document. No real data."
  print ""
  for (c = 1; c <= 40; c++) {
    printf "## Chapter %02d\n\n", c
    for (s = 1; s <= 5; s++) {
      printf "### Section %02d.%d\n\n", c, s
      for (u = 1; u <= 3; u++) {
        printf "#### Unit %02d.%d.%d\n\n", c, s, u
        printf "Unit %02d.%d.%d describes validation rule VR-%05d for the payment flow.\n", c, s, u, c*100+s*10+u
        print "It applies to card, bank transfer and wallet payments in every region."
        print "```"
        printf "# fake heading %02d.%d.%d inside a code fence\n", c, s, u
        print "ERR-4012 Payment declined"
        print "```"
        print "##### too deep to be an outline heading"
        print "#hashtag without a space is not a heading"
        print ""
      }
    }
  }
}' > spike-files/spike-spec.md

ls -l spike-files
printf 'markdown lines=%s bytes=%s\n' "$(wc -l < spike-files/spike-spec.md)" "$(wc -c < spike-files/spike-spec.md)"
echo "Markdown expected: Headings 841 (1 H1, 40 H2, 200 H3, 600 H4); line 1 is the H1; ERR-4012 appears 600 times"
printf 'lines: 1mb=%s 20mb=%s\n' "$(wc -l < spike-files/spike-1mb.log)" "$(wc -l < spike-files/spike-20mb.log)"
echo "Search terms: ERR-4012 (every 5000th line), NEEDLE-UNIQUE-7731 (every 12345th line), ERROR (~1 in 7)"
