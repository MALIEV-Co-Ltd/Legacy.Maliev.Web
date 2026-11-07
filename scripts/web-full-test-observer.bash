# BASH_ENV opt-in for only the immutable full-Test script. Original commands run directly.
if [[ ${_WEB_FULL_OBSERVER_ACTIVE-} == 1 ]]; then
  return 0
fi
_web_observer_source="${BASH_SOURCE[0]%/*}/web-full-test-observer.py"
if python3 -B "$_web_observer_source" check "$0" "$RUNNER_TEMP" 2>/dev/null; then
  if [[ -n "$(trap -p EXIT)" ]]; then
    exit 97
  fi
  export _WEB_FULL_OBSERVER_ACTIVE=1
  _web_observer_dir="$GITHUB_WORKSPACE/full-test-observer-results"
  _web_observer_parent=$BASHPID
  if mkdir -m 700 -- "$_web_observer_dir" 2>/dev/null; then
    python3 -B "$_web_observer_source" run "$_web_observer_parent" "$GITHUB_WORKSPACE" "$RUNNER_TEMP" "$_web_observer_dir" </dev/null 2>/dev/null &
    _web_observer_pid=$!
    _web_observer_cleanup() {
      local _web_observer_status=$1
      trap - EXIT
      if python3 -B "$_web_observer_source" stop "$_web_observer_dir" "$_web_observer_parent" "$_web_observer_pid" 2>/dev/null; then
        wait "$_web_observer_pid" || :
      fi
      exit "$_web_observer_status"
    }
    trap '_web_observer_cleanup "$?"' EXIT
    if ! python3 -B "$_web_observer_source" ready "$_web_observer_dir" "$_web_observer_pid" "$_web_observer_parent" 2>/dev/null; then
      python3 -B "$_web_observer_source" unavailable "$_web_observer_dir" 2>/dev/null || :
    fi
  else
    : # Preflight requires this owned directory to be absent; never overwrite an existing owner.
  fi
fi
