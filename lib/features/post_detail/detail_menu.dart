import 'package:flutter/material.dart';

import '../../core/theme/karar_tokens.dart';

/// Opens a themed popup menu anchored to the widget that owns
/// [anchorContext] (same placement as `PopupMenuButton`). Lets Karar's
/// custom circle/pill buttons open menus without nesting a second gesture
/// detector inside a `PopupMenuButton`.
Future<T?> showAnchoredMenu<T>(
  BuildContext anchorContext, {
  required List<PopupMenuEntry<T>> items,
  T? initialValue,
}) {
  final button = anchorContext.findRenderObject()! as RenderBox;
  final overlay = Navigator.of(anchorContext).overlay!.context.findRenderObject()!
      as RenderBox;
  final position = RelativeRect.fromRect(
    Rect.fromPoints(
      button.localToGlobal(Offset.zero, ancestor: overlay),
      button.localToGlobal(
        button.size.bottomRight(Offset.zero),
        ancestor: overlay,
      ),
    ),
    Offset.zero & overlay.size,
  );
  return showMenu<T>(
    context: anchorContext,
    position: position,
    items: items,
    initialValue: initialValue,
  );
}

/// A popup menu row: rounded icon + label. [destructive] rows use the
/// Haksız text colour (never `Colors.red`).
PopupMenuItem<T> detailMenuItem<T>(
  BuildContext context, {
  required T value,
  required IconData icon,
  required String label,
  bool destructive = false,
}) {
  final kc = context.kc;
  final color = destructive ? kc.haksizText : null;
  return PopupMenuItem<T>(
    value: value,
    child: Row(
      children: [
        Icon(icon, size: 20, color: color ?? kc.text2),
        const SizedBox(width: 12),
        Flexible(
          child: Text(
            label,
            style: color == null ? null : TextStyle(color: color),
          ),
        ),
      ],
    ),
  );
}
