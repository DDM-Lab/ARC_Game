"""
Action Enumerator for ARC Game
Generates all valid actions based on current game state

This module enumerates all possible actions a player can take given:
- Current budget
- Available workers (trained/untrained)
- Building states
- Available construction sites
- Resource inventories
- Vehicle availability
"""

from typing import List, Dict, Any
from dataclasses import dataclass, asdict


# ============================================================================
# ACTION DATA STRUCTURES
# ============================================================================

@dataclass
class Action:
    """Base class for all actions"""
    action_id: str
    action_type: str
    description: str
    cost: int
    requirements: Dict[str, Any]
    effects: Dict[str, Any]

    def to_dict(self) -> dict:
        return asdict(self)


@dataclass
class ConstructionAction(Action):
    """Build a new facility"""
    building_type: str  # Kitchen, Shelter, CaseworkSite
    site_id: int
    site_name: str
    construction_time_days: float
    required_workers: int = 4  # Workers needed to operate after construction

    def to_dict(self) -> dict:
        """Convert to Unity-compatible JSON structure"""
        base = super().to_dict()
        # Nest construction-specific fields under 'construction' key for Unity
        base['construction'] = {
            'building_type': self.building_type,
            'site_id': self.site_id,
            'site_name': self.site_name
        }
        # Remove top-level construction fields
        base.pop('building_type', None)
        base.pop('site_id', None)
        base.pop('site_name', None)
        base.pop('construction_time_days', None)
        base.pop('required_workers', None)
        return base


@dataclass
class WorkerAction(Action):
    """Hire or train workers"""
    worker_action_type: str  # hire_untrained, hire_trained, train_untrained
    quantity: int

    def to_dict(self) -> dict:
        """Convert to Unity-compatible JSON structure"""
        base = super().to_dict()
        base['worker'] = {
            'worker_action_type': self.worker_action_type,
            'quantity': self.quantity
        }
        base.pop('worker_action_type', None)
        base.pop('quantity', None)
        return base


@dataclass
class ResourceTransferAction(Action):
    """Transfer resources between facilities"""
    resource_type: str  # FoodPacks, Population
    quantity: int
    source_facility: str
    destination_facility: str
    requires_vehicle: bool

    def to_dict(self) -> dict:
        """Convert to Unity-compatible JSON structure"""
        base = super().to_dict()
        base['transfer'] = {
            'resource_type': self.resource_type,
            'quantity': self.quantity,
            'source_facility': self.source_facility,
            'destination_facility': self.destination_facility
        }
        base.pop('resource_type', None)
        base.pop('quantity', None)
        base.pop('source_facility', None)
        base.pop('destination_facility', None)
        base.pop('requires_vehicle', None)
        return base


@dataclass
class WorkerAssignmentAction(Action):
    """Assign workers to a building"""
    building_name: str
    worker_type: str  # trained, untrained
    quantity: int

    def to_dict(self) -> dict:
        """Convert to Unity-compatible JSON structure"""
        base = super().to_dict()
        base['assignment'] = {
            'building_name': self.building_name,
            'worker_type': self.worker_type,
            'quantity': self.quantity
        }
        base.pop('building_name', None)
        base.pop('worker_type', None)
        base.pop('quantity', None)
        return base


@dataclass
class DeconstructionAction(Action):
    """Deconstruct a building"""
    building_name: str
    deconstruction_time_days: float
    frees_site_id: int

    def to_dict(self) -> dict:
        """Convert to Unity-compatible JSON structure"""
        base = super().to_dict()
        base['deconstruction'] = {
            'building_name': self.building_name,
            'frees_site_id': self.frees_site_id
        }
        base.pop('building_name', None)
        base.pop('deconstruction_time_days', None)
        base.pop('frees_site_id', None)
        return base


# ============================================================================
# ACTION ENUMERATOR
# ============================================================================

class ActionEnumerator:
    """
    Enumerates all valid actions based on game state
    """

    # Game constants
    UNTRAINED_WORKER_COST = 100
    TRAINED_WORKER_COST = 500
    TRAINING_COST_PER_WORKER = 50
    BUILDING_CONSTRUCTION_COST = 1000
    BUILDING_REQUIRED_WORKFORCE = 4

    BUILDING_TYPES = ["Kitchen", "Shelter", "CaseworkSite"]

    def __init__(self, game_state: dict):
        """
        Initialize with current game state

        Args:
            game_state: Full GameStatePayload from Unity
        """
        self.game_state = game_state
        self.actions: List[Action] = []

    def enumerate_all_actions(self) -> List[Dict[str, Any]]:
        """
        Generate all valid actions for current game state

        Returns:
            List of action dictionaries
        """
        self.actions = []

        # Enumerate all action types
        self._enumerate_construction_actions()
        self._enumerate_worker_actions()
        self._enumerate_resource_transfer_actions()
        self._enumerate_worker_assignment_actions()
        self._enumerate_deconstruction_actions()

        # Convert to dictionaries
        return [action.to_dict() for action in self.actions]

    def _enumerate_construction_actions(self):
        """
        Generate all valid building construction actions
        """
        construction_state = self.game_state.get('constructionState', {})
        daily_metrics = self.game_state.get('dailyMetrics', {})

        budget = daily_metrics.get('currentBudget', 0)
        available_sites = construction_state.get('availableSites', [])
        construction_time = construction_state.get('constructionTimeDays', 1.0)

        # Can only build if we have budget
        if budget < self.BUILDING_CONSTRUCTION_COST:
            return

        # Generate action for each building type at each available site
        for site in available_sites:
            if not site.get('isAvailable', False):
                continue

            site_id = site.get('siteId', -1)
            site_name = site.get('siteName', 'Unknown Site')

            for building_type in self.BUILDING_TYPES:
                action_id = f"build_{building_type}_{site_id}"

                action = ConstructionAction(
                    action_id=action_id,
                    action_type="construction",
                    description=f"Build {building_type} at {site_name}",
                    cost=self.BUILDING_CONSTRUCTION_COST,
                    building_type=building_type,
                    site_id=site_id,
                    site_name=site_name,
                    construction_time_days=construction_time,
                    required_workers=self.BUILDING_REQUIRED_WORKFORCE,
                    requirements={
                        'budget': self.BUILDING_CONSTRUCTION_COST,
                        'available_site': True
                    },
                    effects={
                        'budget': -self.BUILDING_CONSTRUCTION_COST,
                        'new_building': building_type,
                        'site_occupied': site_id
                    }
                )

                self.actions.append(action)

    def _enumerate_worker_actions(self):
        """
        Generate all valid worker hiring and training actions
        """
        workforce_state = self.game_state.get('workforceState', {})
        daily_metrics = self.game_state.get('dailyMetrics', {})

        budget = daily_metrics.get('currentBudget', 0)
        untrained_workers = workforce_state.get('freeUntrainedWorkers', 0)
        new_workers_today = workforce_state.get('newWorkersHiredToday', 0)

        # Daily hiring limit (assuming 5 workers max per day)
        DAILY_HIRE_LIMIT = 5
        can_hire = new_workers_today < DAILY_HIRE_LIMIT

        # 1. Hire Untrained Workers (quantities: 1-5)
        if can_hire and budget >= self.UNTRAINED_WORKER_COST:
            max_affordable = min(
                budget // self.UNTRAINED_WORKER_COST,
                DAILY_HIRE_LIMIT - new_workers_today,
                5  # Maximum 5 at once
            )

            for quantity in range(1, max_affordable + 1):
                action_id = f"hire_untrained_{quantity}"
                total_cost = self.UNTRAINED_WORKER_COST * quantity

                action = WorkerAction(
                    action_id=action_id,
                    action_type="worker",
                    worker_action_type="hire_untrained",
                    description=f"Hire {quantity} untrained worker(s) (${self.UNTRAINED_WORKER_COST} each)",
                    cost=total_cost,
                    quantity=quantity,
                    requirements={
                        'budget': total_cost,
                        'daily_hire_limit': can_hire
                    },
                    effects={
                        'budget': -total_cost,
                        'untrained_workers': quantity,
                        'new_workers_today': quantity
                    }
                )

                self.actions.append(action)

        # 2. Hire Trained Workers (quantities: 1-5)
        if can_hire and budget >= self.TRAINED_WORKER_COST:
            max_affordable = min(
                budget // self.TRAINED_WORKER_COST,
                DAILY_HIRE_LIMIT - new_workers_today,
                5
            )

            for quantity in range(1, max_affordable + 1):
                action_id = f"hire_trained_{quantity}"
                total_cost = self.TRAINED_WORKER_COST * quantity

                action = WorkerAction(
                    action_id=action_id,
                    action_type="worker",
                    worker_action_type="hire_trained",
                    description=f"Hire {quantity} trained worker(s) (${self.TRAINED_WORKER_COST} each)",
                    cost=total_cost,
                    quantity=quantity,
                    requirements={
                        'budget': total_cost,
                        'daily_hire_limit': can_hire
                    },
                    effects={
                        'budget': -total_cost,
                        'trained_workers': quantity,
                        'new_workers_today': quantity
                    }
                )

                self.actions.append(action)

        # 3. Train Untrained Workers (quantities: 1-all available)
        if untrained_workers > 0 and budget >= self.TRAINING_COST_PER_WORKER:
            max_trainable = min(
                untrained_workers,
                budget // self.TRAINING_COST_PER_WORKER,
                10  # Maximum 10 at once
            )

            for quantity in range(1, max_trainable + 1):
                action_id = f"train_workers_{quantity}"
                total_cost = self.TRAINING_COST_PER_WORKER * quantity

                action = WorkerAction(
                    action_id=action_id,
                    action_type="worker",
                    worker_action_type="train_untrained",
                    description=f"Train {quantity} untrained worker(s) (${self.TRAINING_COST_PER_WORKER} each, 3 days)",
                    cost=total_cost,
                    quantity=quantity,
                    requirements={
                        'budget': total_cost,
                        'untrained_workers': quantity
                    },
                    effects={
                        'budget': -total_cost,
                        'untrained_workers': -quantity,
                        'workers_in_training': quantity
                    }
                )

                self.actions.append(action)

    def _enumerate_resource_transfer_actions(self):
        """
        Generate all valid resource transfer actions
        """
        map_state = self.game_state.get('mapState', {})
        logistics = self.game_state.get('logistics', {})

        facilities = map_state.get('facilities', [])
        available_vehicles = logistics.get('availableVehicles', 0)

        if available_vehicles == 0:
            return  # No vehicles available for transfers

        # Generate transfer actions between all facility pairs
        for source in facilities:
            source_name = source.get('facilityName', '')
            source_resources = source.get('resources', {})

            food_available = source_resources.get('foodPacks', 0)
            population_available = source_resources.get('population', 0)

            for destination in facilities:
                dest_name = destination.get('facilityName', '')

                # Skip self-transfers
                if source_name == dest_name:
                    continue

                dest_resources = destination.get('resources', {})
                dest_capacity = dest_resources.get('foodPacksCapacity', 0)
                dest_current = dest_resources.get('foodPacks', 0)
                dest_pop_capacity = dest_resources.get('populationCapacity', 0)
                dest_pop_current = dest_resources.get('population', 0)

                # 1. Food Transfer Actions (quantities: 10, 25, 50, 100)
                if food_available > 0:
                    available_capacity = dest_capacity - dest_current

                    for quantity in [10, 25, 50, 100]:
                        if quantity <= food_available and quantity <= available_capacity:
                            action_id = f"transfer_food_{source_name}_{dest_name}_{quantity}"

                            action = ResourceTransferAction(
                                action_id=action_id,
                                action_type="resource_transfer",
                                description=f"Transfer {quantity} food packs from {source_name} to {dest_name}",
                                cost=0,  # Transfers are free (vehicle already paid for)
                                resource_type="FoodPacks",
                                quantity=quantity,
                                source_facility=source_name,
                                destination_facility=dest_name,
                                requires_vehicle=True,
                                requirements={
                                    'source_food': quantity,
                                    'destination_capacity': quantity,
                                    'available_vehicle': True
                                },
                                effects={
                                    f'{source_name}_food': -quantity,
                                    f'{dest_name}_food': quantity,
                                    'vehicle_in_use': True
                                }
                            )

                            self.actions.append(action)

                # 2. Population Transfer Actions (quantities: 5, 10, 20)
                if population_available > 0:
                    available_capacity = dest_pop_capacity - dest_pop_current

                    for quantity in [5, 10, 20]:
                        if quantity <= population_available and quantity <= available_capacity:
                            action_id = f"transfer_population_{source_name}_{dest_name}_{quantity}"

                            action = ResourceTransferAction(
                                action_id=action_id,
                                action_type="resource_transfer",
                                description=f"Transfer {quantity} people from {source_name} to {dest_name}",
                                cost=0,
                                resource_type="Population",
                                quantity=quantity,
                                source_facility=source_name,
                                destination_facility=dest_name,
                                requires_vehicle=True,
                                requirements={
                                    'source_population': quantity,
                                    'destination_capacity': quantity,
                                    'available_vehicle': True
                                },
                                effects={
                                    f'{source_name}_population': -quantity,
                                    f'{dest_name}_population': quantity,
                                    'vehicle_in_use': True
                                }
                            )

                            self.actions.append(action)

    def _enumerate_worker_assignment_actions(self):
        """
        Generate all valid worker assignment actions
        """
        map_state = self.game_state.get('mapState', {})
        workforce_state = self.game_state.get('workforceState', {})

        facilities = map_state.get('facilities', [])
        free_trained = workforce_state.get('freeTrainedWorkers', 0)
        free_untrained = workforce_state.get('freeUntrainedWorkers', 0)

        print(f"🔍 Worker Assignment Debug:")
        print(f"   Free trained workers: {free_trained}")
        print(f"   Free untrained workers: {free_untrained}")
        print(f"   Total facilities: {len(facilities)}")

        # Find buildings that need workers
        for facility in facilities:
            building_status = facility.get('buildingStatus', '')
            building_name = facility.get('facilityName', '')
            assigned = facility.get('assignedWorkforce', 0)
            required = facility.get('requiredWorkforce', 4)
            needed = required - assigned

            print(f"   Building: {building_name}, Status: {building_status}, Workers: {assigned}/{required}")

            # Allow worker assignment for both NeedWorker and InUse buildings that are understaffed
            # (InUse buildings can be partially staffed and still accept more workers)
            if building_status not in ['NeedWorker', 'InUse']:
                continue

            if needed <= 0:
                continue

            # Assign trained workers (quantities: 1-needed)
            if free_trained > 0:
                max_assignable = min(free_trained, needed)

                for quantity in range(1, max_assignable + 1):
                    action_id = f"assign_trained_{building_name}_{quantity}"

                    action = WorkerAssignmentAction(
                        action_id=action_id,
                        action_type="worker_assignment",
                        description=f"Assign {quantity} trained worker(s) to {building_name}",
                        cost=0,
                        building_name=building_name,
                        worker_type="trained",
                        quantity=quantity,
                        requirements={
                            'free_trained_workers': quantity,
                            'building_needs_workers': needed
                        },
                        effects={
                            'free_trained_workers': -quantity,
                            f'{building_name}_workforce': quantity
                        }
                    )

                    self.actions.append(action)

            # Assign untrained workers (quantities: 1-needed)
            if free_untrained > 0:
                max_assignable = min(free_untrained, needed)

                for quantity in range(1, max_assignable + 1):
                    action_id = f"assign_untrained_{building_name}_{quantity}"

                    action = WorkerAssignmentAction(
                        action_id=action_id,
                        action_type="worker_assignment",
                        description=f"Assign {quantity} untrained worker(s) to {building_name}",
                        cost=0,
                        building_name=building_name,
                        worker_type="untrained",
                        quantity=quantity,
                        requirements={
                            'free_untrained_workers': quantity,
                            'building_needs_workers': needed
                        },
                        effects={
                            'free_untrained_workers': -quantity,
                            f'{building_name}_workforce': quantity
                        }
                    )

                    self.actions.append(action)

    def _enumerate_deconstruction_actions(self):
        """
        Generate all valid building deconstruction actions
        """
        map_state = self.game_state.get('mapState', {})
        construction_state = self.game_state.get('constructionState', {})

        facilities = map_state.get('facilities', [])
        deconstruction_time = construction_state.get('deconstructionTimeDays', 1.0)

        # Can only deconstruct player-built buildings
        for facility in facilities:
            facility_type = facility.get('facilityType', '')

            # Only "Building" type (not "Prebuilt") can be deconstructed
            if facility_type != 'Building':
                continue

            building_name = facility.get('facilityName', '')
            building_status = facility.get('buildingStatus', '')
            original_site_id = facility.get('originalSiteId', -1)
            assigned_workers = facility.get('assignedWorkforce', 0)

            action_id = f"deconstruct_{building_name}"

            # Note: Deconstruction frees workers and the site
            action = DeconstructionAction(
                action_id=action_id,
                action_type="deconstruction",
                description=f"Deconstruct {building_name} (frees {assigned_workers} workers and site)",
                cost=0,  # Deconstruction is free
                building_name=building_name,
                deconstruction_time_days=deconstruction_time,
                frees_site_id=original_site_id,
                requirements={
                    'is_player_building': True
                },
                effects={
                    'building_removed': building_name,
                    'site_freed': original_site_id,
                    'workers_freed': assigned_workers
                }
            )

            self.actions.append(action)

    def get_action_summary(self) -> Dict[str, int]:
        """
        Get summary statistics of enumerated actions

        Returns:
            Dictionary with counts per action type
        """
        summary = {
            'total': len(self.actions),
            'construction': 0,
            'worker': 0,
            'resource_transfer': 0,
            'worker_assignment': 0,
            'deconstruction': 0
        }

        for action in self.actions:
            action_type = action.action_type
            if action_type in summary:
                summary[action_type] += 1

        return summary


# ============================================================================
# HELPER FUNCTIONS
# ============================================================================

def enumerate_actions(game_state: dict) -> List[Dict[str, Any]]:
    """
    Convenience function to enumerate all actions

    Args:
        game_state: Full GameStatePayload from Unity

    Returns:
        List of action dictionaries
    """
    enumerator = ActionEnumerator(game_state)
    return enumerator.enumerate_all_actions()


def get_action_space_size(game_state: dict) -> int:
    """
    Get the total number of valid actions for current game state

    Args:
        game_state: Full GameStatePayload from Unity

    Returns:
        Number of valid actions
    """
    enumerator = ActionEnumerator(game_state)
    enumerator.enumerate_all_actions()
    return len(enumerator.actions)


# ============================================================================
# MAIN (for testing)
# ============================================================================

if __name__ == "__main__":
    # Example game state for testing
    test_game_state = {
        'dailyMetrics': {
            'currentBudget': 5000,
            'currentSatisfaction': 75
        },
        'workforceState': {
            'freeTrainedWorkers': 3,
            'freeUntrainedWorkers': 5,
            'workingTrainedWorkers': 8,
            'workingUntrainedWorkers': 4,
            'newWorkersHiredToday': 2
        },
        'constructionState': {
            'availableSites': [
                {'siteId': 1, 'siteName': 'North Site', 'isAvailable': True},
                {'siteId': 2, 'siteName': 'South Site', 'isAvailable': True}
            ],
            'constructionTimeDays': 2.0,
            'deconstructionTimeDays': 1.0
        },
        'mapState': {
            'facilities': [
                {
                    'facilityName': 'Kitchen Alpha',
                    'facilityType': 'Building',
                    'buildingStatus': 'InUse',
                    'resources': {
                        'foodPacks': 100,
                        'foodPacksCapacity': 200,
                        'population': 0,
                        'populationCapacity': 0
                    },
                    'assignedWorkforce': 4,
                    'requiredWorkforce': 4
                },
                {
                    'facilityName': 'Shelter Beta',
                    'facilityType': 'Building',
                    'buildingStatus': 'NeedWorker',
                    'resources': {
                        'foodPacks': 0,
                        'foodPacksCapacity': 0,
                        'population': 20,
                        'populationCapacity': 50
                    },
                    'assignedWorkforce': 2,
                    'requiredWorkforce': 4
                }
            ]
        },
        'logistics': {
            'availableVehicles': 3,
            'vehiclesInTransit': 1
        }
    }

    print("="*80)
    print("ACTION ENUMERATOR TEST")
    print("="*80)

    enumerator = ActionEnumerator(test_game_state)
    actions = enumerator.enumerate_all_actions()

    summary = enumerator.get_action_summary()

    print(f"\nTotal Actions: {summary['total']}")
    print(f"  - Construction: {summary['construction']}")
    print(f"  - Worker: {summary['worker']}")
    print(f"  - Resource Transfer: {summary['resource_transfer']}")
    print(f"  - Worker Assignment: {summary['worker_assignment']}")
    print(f"  - Deconstruction: {summary['deconstruction']}")

    print("\n" + "="*80)
    print("SAMPLE ACTIONS (first 10)")
    print("="*80)

    for i, action in enumerate(actions[:10]):
        print(f"\n{i+1}. {action['description']}")
        print(f"   Type: {action['action_type']}, Cost: ${action['cost']}")
        print(f"   Effects: {action['effects']}")
