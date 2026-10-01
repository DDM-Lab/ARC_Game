"""Swap router functions for fakes in tests. The Session is split across several modules
(router.session and its mixins), each importing what it uses, so a fake must be installed in every
module that holds the name; patch() does that, and originals()/restore() undo it."""
import router.common
import router.devpanel
import router.messaging
import router.officer_loop
import router.officer_tools
import router.plugin_context
import router.proposals
import router.session
import router.standing_orders
import router.unity_io

MODULES = (router.session, router.common, router.unity_io, router.officer_loop, router.officer_tools,
           router.proposals, router.standing_orders, router.messaging, router.devpanel,
           router.plugin_context)


def patch(name, fn):
    """Install `fn` as `name` in every router module that uses it."""
    hits = [m for m in MODULES if hasattr(m, name)]
    assert hits, f"no router module uses {name!r}"
    for m in hits:
        setattr(m, name, fn)


def originals(*names) -> dict:
    return {(m, n): getattr(m, n) for m in MODULES for n in names if hasattr(m, n)}


def restore(saved: dict):
    for (m, n), fn in saved.items():
        setattr(m, n, fn)
