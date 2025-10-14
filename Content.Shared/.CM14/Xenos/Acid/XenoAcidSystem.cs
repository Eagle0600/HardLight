﻿using Content.Shared.Coordinates;
using Content.Shared.DoAfter;
using Content.Shared.Popups;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.CM14.Xenos.Acid;

public sealed class XenoAcidSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly XenoSystem _xeno = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<XenoComponent, XenoCorrosiveAcidEvent>(OnXenoCorrosiveAcid);
        SubscribeLocalEvent<XenoComponent, XenoCorrosiveAcidDoAfterEvent>(OnXenoCorrosiveAcidDoAfter);
        SubscribeLocalEvent<CorrodingComponent, EntityUnpausedEvent>(OnCorrodingUnpaused);
    }

    private void OnXenoCorrosiveAcid(Entity<XenoComponent> xeno, ref XenoCorrosiveAcidEvent args)
    {
        if (!CheckCorrodablePopups(xeno, args.Target))
            return;

        var doAfter = new DoAfterArgs(EntityManager, xeno, xeno.Comp.AcidDelay, new XenoCorrosiveAcidDoAfterEvent(args), xeno, args.Target)
        {
            BreakOnTargetMove = true,
            BreakOnUserMove = true
        };
        _doAfter.TryStartDoAfter(doAfter);
    }

    private void OnXenoCorrosiveAcidDoAfter(Entity<XenoComponent> xeno, ref XenoCorrosiveAcidDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target)
            return;

        if (!CheckCorrodablePopups(xeno, target))
            return;

        if (!_xeno.TryRemovePlasmaPopup(xeno, args.PlasmaCost))
            return;

        if (_net.IsClient)
            return;

        var acid = SpawnAttachedTo(args.AcidId, target.ToCoordinates());
        AddComp(target, new CorrodingComponent
        {
            Acid = acid,
            CorrodesAt = _timing.CurTime + args.Time
        });
    }

    private void OnCorrodingUnpaused(Entity<CorrodingComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.CorrodesAt += args.PausedTime;
    }

    private bool CheckCorrodablePopups(Entity<XenoComponent> xeno, EntityUid target)
    {
        if (!HasComp<CorrodableComponent>(target))
        {
            _popup.PopupClient(Loc.GetString("cm-xeno-acid-not-corrodable", ("target", target)), xeno, xeno);
            return false;
        }

        if (HasComp<CorrodingComponent>(target))
        {
            _popup.PopupClient(Loc.GetString("cm-xeno-acid-already-corroding", ("target", target)), xeno, xeno);
            return false;
        }

        return true;
    }

    public override void Update(float frameTime)
    {
        if (_net.IsClient)
            return;

        var query = EntityQueryEnumerator<CorrodingComponent>();
        var time = _timing.CurTime;

        while (query.MoveNext(out var uid, out var corroding))
        {
            if (time < corroding.CorrodesAt)
                continue;

            QueueDel(uid);
            QueueDel(corroding.Acid);
        }
    }
}
