import 'package:flutter/material.dart';

import '../../core/theme/app_colors.dart';
import 'admin_service.dart';

class AdminDevicesScreen extends StatefulWidget {
  const AdminDevicesScreen({super.key, required this.adminService});
  final AdminService adminService;

  @override
  State<AdminDevicesScreen> createState() => _AdminDevicesScreenState();
}

class _AdminDevicesScreenState extends State<AdminDevicesScreen> {
  List<AdminDevice> _items = [];
  bool _loading = true;
  String? _error;
  final _searchCtrl = TextEditingController();

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _searchCtrl.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() { _loading = true; _error = null; });
    try {
      final q = _searchCtrl.text.trim();
      final result = await widget.adminService.fetchDevices(
          search: q.isEmpty ? null : q);
      setState(() => _items = result.items);
    } catch (e) {
      setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  void _ban(AdminDevice device) {
    showDialog<void>(
      context: context,
      builder: (_) => _BanDeviceDialog(
        deviceId: device.id,
        onConfirm: (reason, type, days) async {
          await widget.adminService.banDevice(
            device.id,
            reason: reason,
            type: type,
            durationDays: days,
          );
          _load();
        },
      ),
    );
  }

  Future<void> _unban(AdminDevice device) async {
    await widget.adminService.unbanDevice(device.id);
    _load();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0F172A),
      appBar: AppBar(
        backgroundColor: const Color(0xFF1E293B),
        title: const Text('Cihazlar', style: TextStyle(color: Colors.white)),
      ),
      body: Column(
        children: [
          Padding(
            padding: const EdgeInsets.all(16),
            child: TextField(
              controller: _searchCtrl,
              style: const TextStyle(color: Colors.white),
              decoration: InputDecoration(
                hintText: 'Cihaz ID ile ara...',
                hintStyle: const TextStyle(color: Colors.white38),
                prefixIcon: const Icon(Icons.search, color: Colors.white38),
                filled: true,
                fillColor: const Color(0xFF1E293B),
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(10),
                  borderSide: BorderSide.none,
                ),
                suffixIcon: IconButton(
                  icon: const Icon(Icons.arrow_forward, color: Colors.white54),
                  onPressed: _load,
                ),
              ),
              onSubmitted: (_) => _load(),
            ),
          ),
          Expanded(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _error != null
                    ? Center(child: Text(_error!,
                        style: const TextStyle(color: Colors.white54)))
                    : _items.isEmpty
                        ? const Center(child: Text('Cihaz bulunamadı.',
                            style: TextStyle(color: Colors.white54)))
                        : ListView.separated(
                            padding: const EdgeInsets.symmetric(horizontal: 16),
                            itemCount: _items.length,
                            separatorBuilder: (_, __) =>
                                const SizedBox(height: 8),
                            itemBuilder: (_, i) => _DeviceRow(
                              device: _items[i],
                              onBan: () => _ban(_items[i]),
                              onUnban: () => _unban(_items[i]),
                            ),
                          ),
          ),
        ],
      ),
    );
  }
}

class _DeviceRow extends StatelessWidget {
  const _DeviceRow(
      {required this.device, required this.onBan, required this.onUnban});
  final AdminDevice device;
  final VoidCallback onBan;
  final VoidCallback onUnban;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: const Color(0xFF1E293B),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(
          color: device.isBanned
              ? AppColors.haksiz.withValues(alpha: 0.3)
              : Colors.white.withValues(alpha: 0.07),
        ),
      ),
      child: Row(
        children: [
          const Icon(Icons.phone_android, color: Colors.white38, size: 20),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  device.id.length > 16
                      ? '${device.id.substring(0, 16)}...'
                      : device.id,
                  style: const TextStyle(
                      color: Colors.white,
                      fontSize: 12,
                      fontFamily: 'monospace'),
                ),
                const SizedBox(height: 2),
                Row(
                  children: [
                    Text('${device.postCount} post · ${device.reportCount} rapor',
                        style: const TextStyle(
                            color: Colors.white38, fontSize: 11)),
                    if (device.isBanned) ...[
                      const SizedBox(width: 8),
                      Container(
                        padding: const EdgeInsets.symmetric(
                            horizontal: 6, vertical: 2),
                        decoration: BoxDecoration(
                          color: AppColors.haksiz.withValues(alpha: 0.15),
                          borderRadius: BorderRadius.circular(4),
                        ),
                        child: const Text('BANLANDI',
                            style: TextStyle(
                                color: AppColors.haksiz,
                                fontSize: 10,
                                fontWeight: FontWeight.w700)),
                      ),
                    ],
                  ],
                ),
              ],
            ),
          ),
          if (!device.isBanned)
            IconButton(
              icon: const Icon(Icons.block, color: AppColors.haksiz, size: 20),
              onPressed: onBan,
              tooltip: 'Banla',
            )
          else
            IconButton(
              icon: const Icon(Icons.check_circle_outline,
                  color: AppColors.hakli, size: 20),
              onPressed: onUnban,
              tooltip: 'Banı Kaldır',
            ),
        ],
      ),
    );
  }
}

class _BanDeviceDialog extends StatefulWidget {
  const _BanDeviceDialog(
      {required this.deviceId, required this.onConfirm});
  final String deviceId;
  final Future<void> Function(String reason, String type, int? days) onConfirm;

  @override
  State<_BanDeviceDialog> createState() => _BanDeviceDialogState();
}

class _BanDeviceDialogState extends State<_BanDeviceDialog> {
  final _ctrl = TextEditingController();
  String _type = 'temporary';
  int _days = 7;
  bool _loading = false;

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      backgroundColor: const Color(0xFF1E293B),
      title: const Text('Cihazı Banla', style: TextStyle(color: Colors.white)),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(
            controller: _ctrl,
            style: const TextStyle(color: Colors.white),
            decoration: const InputDecoration(
              hintText: 'Ban sebebi',
              hintStyle: TextStyle(color: Colors.white38),
              enabledBorder: UnderlineInputBorder(
                  borderSide: BorderSide(color: Colors.white24)),
            ),
          ),
          const SizedBox(height: 16),
          Row(
            children: [
              const Text('Tür:', style: TextStyle(color: Colors.white70)),
              const SizedBox(width: 12),
              DropdownButton<String>(
                value: _type,
                dropdownColor: const Color(0xFF1E293B),
                style: const TextStyle(color: Colors.white),
                onChanged: (v) => setState(() => _type = v!),
                items: const [
                  DropdownMenuItem(value: 'temporary', child: Text('Geçici')),
                  DropdownMenuItem(value: 'permanent', child: Text('Kalıcı')),
                ],
              ),
            ],
          ),
          if (_type == 'temporary') ...[
            const SizedBox(height: 8),
            Row(
              children: [
                const Text('Süre:', style: TextStyle(color: Colors.white70)),
                const SizedBox(width: 12),
                DropdownButton<int>(
                  value: _days,
                  dropdownColor: const Color(0xFF1E293B),
                  style: const TextStyle(color: Colors.white),
                  onChanged: (v) => setState(() => _days = v!),
                  items: const [
                    DropdownMenuItem(value: 1, child: Text('1 gün')),
                    DropdownMenuItem(value: 7, child: Text('7 gün')),
                    DropdownMenuItem(value: 30, child: Text('30 gün')),
                    DropdownMenuItem(value: 90, child: Text('90 gün')),
                  ],
                ),
              ],
            ),
          ],
        ],
      ),
      actions: [
        TextButton(
            onPressed: () => Navigator.pop(context),
            child: const Text('İptal', style: TextStyle(color: Colors.white54))),
        FilledButton(
          style: FilledButton.styleFrom(backgroundColor: AppColors.haksiz),
          onPressed: _loading
              ? null
              : () async {
                  setState(() => _loading = true);
                  await widget.onConfirm(
                    _ctrl.text.trim(),
                    _type,
                    _type == 'temporary' ? _days : null,
                  );
                  if (context.mounted) Navigator.pop(context);
                },
          child: const Text('Banla'),
        ),
      ],
    );
  }
}
