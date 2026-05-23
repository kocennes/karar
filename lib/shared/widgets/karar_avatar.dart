import 'package:flutter/material.dart';

class KararAvatar extends StatelessWidget {
  const KararAvatar({
    super.key,
    required this.username,
    this.radius = 18,
    this.fontSize,
  });

  final String username;
  final double radius;
  final double? fontSize;

  @override
  Widget build(BuildContext context) {
    return CircleAvatar(
      radius: radius,
      backgroundColor: _userColor(username),
      child: Text(
        username.isNotEmpty ? username[0].toUpperCase() : '?',
        style: TextStyle(
          color: Colors.white,
          fontWeight: FontWeight.w800,
          fontSize: fontSize ?? (radius * 0.8),
        ),
      ),
    );
  }

  Color _userColor(String username) {
    final colors = [
      const Color(0xFF6366F1), // Indigo
      const Color(0xFF8B5CF6), // Violet
      const Color(0xFFEC4899), // Pink
      const Color(0xFFF43F5E), // Rose
      const Color(0xFFF59E0B), // Amber
      const Color(0xFF10B981), // Emerald
      const Color(0xFF06B6D4), // Cyan
      const Color(0xFF3B82F6), // Blue
    ];

    if (username.isEmpty) return colors[0];

    // Simple hash to pick a color
    final hash = username.codeUnits.reduce((a, b) => a + b);
    return colors[hash % colors.length];
  }
}
