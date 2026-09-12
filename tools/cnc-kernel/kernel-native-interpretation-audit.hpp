#pragma once
#include <chrono>
#include <algorithm>
#include <map>
#include <string>
#include <vector>

namespace MalievNativeAudit {
using Clock = std::chrono::steady_clock;
inline double Milliseconds(Clock::duration duration) {
  return std::chrono::duration<double, std::milli>(duration).count();
}
struct Phase { size_t calls = 0, exits = 0; double inclusiveMs = 0, selfMs = 0; };
struct Mark {
  std::string name;
  int boundaryIndex = -1;
  double elapsedMs = 0, remainingMs = 0;
};
struct Scope;
struct Metric {
  int boundaryIndex = -1;
  std::string obligationId, method, status;
  double elapsedMs = 0;
  int intervals = 0;
};
struct Audit {
  Clock::time_point origin = Clock::now();
  std::map<std::string, Phase> phases;
  std::map<std::string, Mark> marks;
  std::vector<Mark> markOccurrences;
  std::vector<Metric> slowMetrics;
  std::map<std::string, size_t> counters;
  size_t identityCalls = 0, identityCandidates = 0, captureCalls = 0;
  double identityMs = 0;
  std::string firstCancellationSite;
  Mark firstCancellation;
  Scope *current = nullptr;
  void ObserveMetric(const Metric &metric) {
    slowMetrics.push_back(metric);
    std::stable_sort(slowMetrics.begin(), slowMetrics.end(),
        [](const Metric &a, const Metric &b) { return a.elapsedMs > b.elapsedMs; });
    if (slowMetrics.size() > 20) slowMetrics.resize(20);
  }
  void Checkpoint(const char *name, Clock::time_point deadline, int boundary) {
    const auto now = Clock::now();
    Mark mark;
    mark.name = name; mark.boundaryIndex = boundary;
    mark.elapsedMs = Milliseconds(now - origin);
    mark.remainingMs = Milliseconds(deadline - now);
    marks.emplace(name, mark);
    markOccurrences.push_back(mark);
  }
};
// Scope lifetime must end before the owning invocation Evidence is reset.
struct Scope {
  Audit &audit;
  std::string name;
  Clock::time_point started;
  Scope *parent;
  double childrenMs = 0;
  Scope(Audit &value, const std::string &phase)
      : audit(value), name(phase), started(Clock::now()), parent(value.current) {
    audit.current = this;
    ++audit.phases[name].calls;
  }
  ~Scope() {
    const double elapsed = Milliseconds(Clock::now() - started);
    auto &phase = audit.phases[name];
    ++phase.exits; phase.inclusiveMs += elapsed;
    phase.selfMs += elapsed > childrenMs ? elapsed - childrenMs : 0;
    if (parent) parent->childrenMs += elapsed;
    audit.current = parent;
  }
};
} // namespace MalievNativeAudit
